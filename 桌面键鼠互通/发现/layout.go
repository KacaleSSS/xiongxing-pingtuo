package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"github.com/pelletier/go-toml/v2"
	"os"

	"path/filepath"
	"strings"
	"sync"
	"time"
)

func edgePath(name string) string { return filepath.Join(os.Getenv("LOCALAPPDATA"), "EdgeMouse", name) }

var rolesMutex sync.Mutex

func loadRoles() (map[string]string, error) {
	data, e := os.ReadFile(edgePath("desktop-peers.json"))
	roles := map[string]string{}
	if errors.Is(e, os.ErrNotExist) {
		return roles, nil
	}
	if e != nil {
		return nil, e
	}
	e = json.Unmarshal(data, &roles)
	return roles, e
}
func rememberRole(id, role string) error {
	rolesMutex.Lock()
	defer rolesMutex.Unlock()
	roles, e := loadRoles()
	if e != nil {
		return e
	}
	if roles[id] == role {
		return nil
	}
	roles[id] = role
	data, e := json.Marshal(roles)
	if e != nil {
		return e
	}
	path := edgePath("desktop-peers.json")
	if e = os.MkdirAll(filepath.Dir(path), 0700); e != nil {
		return e
	}
	tmp, e := os.CreateTemp(filepath.Dir(path), ".peer-")
	if e != nil {
		return e
	}
	defer os.Remove(tmp.Name())
	_, e = tmp.Write(data)
	ce := tmp.Close()
	if e != nil {
		return e
	}
	if ce != nil {
		return ce
	}
	return os.Rename(tmp.Name(), path)
}
func layoutDock(role string) (string, string) {
	var layout struct{ Mac, PC struct{ Side, Slot string } }
	data, _ := os.ReadFile(edgePath("layout.json"))
	_ = json.Unmarshal(data, &layout)
	if role == "windows" {
		if opposite[layout.PC.Side] != "" {
			slot := layout.PC.Slot
			if opposite[slot] == "" {
				slot = layout.PC.Side
			}
			return layout.PC.Side, slot
		}
		return "top", "top"
	}
	return layout.Mac.Side, layout.Mac.Side
}

// Change all managed entries in one write so an exchange of two directions has no temporary collision.
func layoutSide(role string) string { _, slot := layoutDock(role); return slot }
func (m machine) applyLocalLayout() error {
	rolesMutex.Lock()
	defer rolesMutex.Unlock()
	roles, e := loadRoles()
	if e != nil {
		return e
	}
	path := filepath.Join(m.dir, "config.toml")
	doc, old, e := readConfig(path)
	if e != nil {
		return e
	}
	auth, _ := doc["authorized_fingerprints"].(map[string]any)
	expected := map[string]string{}
	for key, v := range auth {
		id := strings.ReplaceAll(key, ":", "")
		label, ok := v.(string)
		if !ok || !validID(id) || label != "edge-"+id[:16] {
			continue
		}
		if side := layoutSide(roles[id]); opposite[side] != "" {
			expected[label] = side
		}
	}
	cs, _ := doc["clients"].([]any)
	occupied := map[string]bool{}
	active := map[string]string{}
	changed := false
	for _, v := range cs {
		c, ok := v.(map[string]any)
		if !ok {
			return fmt.Errorf("无法读取客户端记录")
		}
		label, _ := c["hostname"].(string)
		side, _ := c["position"].(string)
		if want, ok := expected[label]; ok {
			if side != want {
				side = want
				c["position"] = want
				changed = true
			}
			if c["activate_on_startup"] == true {
				active[label] = side
			}
		}
		if c["activate_on_startup"] == true {
			if occupied[side] {
				return fmt.Errorf("方向 %s 已有另一台活动桌面", side)
			}
			occupied[side] = true
		}
	}
	if changed {
		out, e := toml.Marshal(doc)
		if e != nil {
			return e
		}
		if e = (plan{path: path, before: old, after: out}).apply(); e != nil {
			return e
		}
	}
	if len(active) == 0 {
		return nil
	}
	for i := 0; i < 12; i++ {
		out, e := engineCommand(m.engine, "cli", "list").Output()
		if e == nil {
			ok := true
			for name, side := range active {
				if !strings.Contains(string(out), name+":4242 ("+side+") active: true") {
					ok = false
				}
			}
			if ok {
				return nil
			}
		}
		time.Sleep(200 * time.Millisecond)
	}
	return fmt.Errorf("配置已保存，尚未收到引擎生效回执")
}
