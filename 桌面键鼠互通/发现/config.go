package main

import (
	"bytes"
	"crypto/sha256"
	"crypto/tls"
	"encoding/hex"
	"errors"
	"fmt"
	"net"
	"os"

	"path/filepath"
	"strings"
	"time"

	"github.com/pelletier/go-toml/v2"
)

type machine struct {
	routes                                  []route
	segment                                 *edgeSegment
	dir, engine, side, preferred, geometric string
	cert                                    tls.Certificate
}

func fingerprint(der []byte) string { sum := sha256.Sum256(der); return hex.EncodeToString(sum[:]) }
func validID(s string) bool {
	b, e := hex.DecodeString(s)
	return e == nil && len(b) == 32 && strings.ToLower(s) == s
}
func colon(id string) string {
	var parts []string
	for i := 0; i < len(id); i += 2 {
		parts = append(parts, id[i:i+2])
	}
	return strings.Join(parts, ":")
}
func identity(path string) (tls.Certificate, error) {
	raw, e := os.ReadFile(path)
	if e != nil {
		return tls.Certificate{}, e
	}
	// webrtc-dtls writes PKCS#8 under a nonstandard PRIVATE_KEY PEM label.
	return tls.X509KeyPair(raw, bytes.ReplaceAll(raw, []byte("PRIVATE_KEY"), []byte("PRIVATE KEY")))
}

type plan struct {
	path          string
	before, after []byte
	side          string
}

func readConfig(path string) (map[string]any, []byte, error) {
	raw, e := os.ReadFile(path)
	if e != nil && !errors.Is(e, os.ErrNotExist) {
		return nil, nil, e
	}
	doc := map[string]any{}
	e = toml.Unmarshal(raw, &doc)
	return doc, raw, e
}
func trusted(dir, id string) bool {
	doc, _, e := readConfig(filepath.Join(dir, "config.toml"))
	if e != nil {
		return false
	}
	keys, _ := doc["authorized_fingerprints"].(map[string]any)
	_, ok := keys[colon(id)]
	return ok
}
func (m machine) prepare(id string, ip net.IP) (plan, error) {
	return m.prepareReplacement(id, ip, false)
}
func (m machine) prepareReplacement(id string, ip net.IP, replace bool) (plan, error) {
	path := filepath.Join(m.dir, "config.toml")
	doc, raw, e := readConfig(path)
	if e != nil {
		return plan{}, e
	}
	auth, _ := doc["authorized_fingerprints"].(map[string]any)
	if auth == nil {
		if doc["authorized_fingerprints"] != nil {
			return plan{}, errors.New("证书配置格式异常")
		}
		auth = map[string]any{}
		doc["authorized_fingerprints"] = auth
	}
	label := "edge-" + id[:16]
	old, isTrusted := auth[colon(id)].(string)
	clients, _ := doc["clients"].([]any)
	if doc["clients"] != nil && clients == nil {
		return plan{}, errors.New("设备配置格式异常")
	}
	var selected map[string]any
	selectedIndex := -1
	for i, entry := range clients {
		c, ok := entry.(map[string]any)
		if !ok {
			return plan{}, errors.New("设备记录格式异常")
		}
		// The earlier file-pairing route used the peer hostname or one unnamed IP.
		prevHost, _ := c["hostname"].(string)
		ips, _ := c["ips"].([]any)
		legacy := isTrusted && ((prevHost != "" && strings.EqualFold(prevHost, old)) || (prevHost == "" && len(ips) == 1 && (ips[0] == ip.String() || (old == "Windows" && len(clients) == 1))))
		if prevHost == label || legacy {
			if selected != nil {
				return plan{}, errors.New("存在重复设备记录，请检查已保存的设备配置")
			}
			selected = c
			selectedIndex = i
		}
	}
	side := m.side
	if selected != nil {
		if s, ok := selected["position"].(string); ok {
			side = s
		}
	}
	if m.preferred != "" {
		side = m.preferred
	}
	for i, entry := range clients {
		c := entry.(map[string]any)
		if i == selectedIndex {
			continue
		}
		if c["position"] == side && c["activate_on_startup"] == true {
			host, _ := c["hostname"].(string)
			if replace && strings.HasPrefix(host, "edge-") {
				for key, desc := range auth {
					raw := strings.ReplaceAll(key, ":", "")
					if validID(raw) && host == "edge-"+raw[:16] && desc == host {
						delete(auth, key)
						c["activate_on_startup"] = false
						break
					}
				}
				if c["activate_on_startup"] == false {
					continue
				}
			}
			return plan{}, fmt.Errorf("%s 边缘已分配给其他设备", side)
		}
	}
	if selected == nil {
		selected = map[string]any{}
		clients = append(clients, selected)
		doc["clients"] = clients
	}
	if p, exists := selected["port"]; exists && fmt.Sprint(p) != "4242" {
		return plan{}, errors.New("已有设备使用自定义端口，请检查已保存的设备配置")
	}
	ips, _ := selected["ips"].([]any)
	if selected["position"] == side && isTrusted && old == label && selected["hostname"] == label && len(ips) == 1 && ips[0] == ip.String() && selected["activate_on_startup"] == true {
		return plan{path, raw, raw, side}, nil
	}
	auth[colon(id)] = label
	selected["hostname"] = label
	selected["ips"] = []string{ip.String()}
	selected["position"] = side
	selected["activate_on_startup"] = true
	out, e := toml.Marshal(doc)
	return plan{path, raw, out, side}, e
}
func (p plan) apply() error {
	current, e := os.ReadFile(p.path)
	if e != nil && !errors.Is(e, os.ErrNotExist) {
		return e
	}
	if !bytes.Equal(p.before, current) {
		return errors.New("配置刚被修改，请重新连接")
	}
	if bytes.Equal(p.before, p.after) {
		return nil
	}
	f, e := os.CreateTemp(filepath.Dir(p.path), ".pair-*.tmp")
	if e != nil {
		return e
	}
	defer os.Remove(f.Name())
	_, e = f.Write(p.after)
	closeErr := f.Close()
	if e != nil {
		return e
	}
	if closeErr != nil {
		return closeErr
	}
	return os.Rename(f.Name(), p.path)
}
func (m machine) install(p plan, id string, ip net.IP) error {
	if e := p.apply(); e != nil {
		return e
	}
	target := "edge-" + id[:16] + ":4242 (" + p.side + ") active: true"
	for i := 0; i < 12; i++ {
		out, e := engineCommand(m.engine, "cli", "list").Output()
		if e == nil {
			for _, line := range strings.Split(string(out), "\n") {
				if strings.Contains(line, target) && strings.Contains(line, ip.String()) {
					return nil
				}
			}
		}
		time.Sleep(250 * time.Millisecond)
	}
	return errors.New("配置已保存，输入服务尚未报告设备已启用，请重新打开桌面应用")
}
