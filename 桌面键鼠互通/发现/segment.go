package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

type edgeSegment struct{ Start, End int }

const edgeUnits = 1000000

func (s edgeSegment) valid() bool { return s.Start >= 0 && s.End <= edgeUnits && s.Start < s.End }
func returnSegment(slot string) (*edgeSegment, error) {
	data, e := os.ReadFile(edgePath("desktop-edges.txt"))
	if e != nil {
		return nil, e
	}
	for _, line := range strings.Split(string(data), "\n") {
		var key, side string
		var l, t, r, b, lo, hi int
		if n, _ := fmt.Sscan(line, &key, &side, &l, &t, &r, &b, &lo, &hi); n != 8 || strings.TrimPrefix(key, "@") != slot {
			continue
		}
		origin, span := l, r-l
		if side == "left" || side == "right" {
			origin, span = t, b-t
		}
		if span <= 0 {
			break
		}
		if strings.HasPrefix(key, "@") {
			return &edgeSegment{0, edgeUnits}, nil
		}
		s := edgeSegment{(lo - origin) * edgeUnits / span, (hi - origin) * edgeUnits / span}
		if s.valid() {
			return &s, nil
		}
	}
	return nil, fmt.Errorf("Mac 的布局锚点不可用，请检查屏幕连接")
}
func saveReturnEdge(dir, side string, s edgeSegment) error {
	path := filepath.Join(dir, "return-edge.txt")
	data := fmt.Sprintf("%s %d %d\n", side, s.Start, s.End)
	old, _ := os.ReadFile(path)
	if string(old) == data {
		return nil
	}
	f, e := os.CreateTemp(dir, ".return-*")
	if e != nil {
		return e
	}
	defer os.Remove(f.Name())
	_, e = f.WriteString(data)
	ce := f.Close()
	if e != nil {
		return e
	}
	if ce != nil {
		return ce
	}
	return os.Rename(f.Name(), path)
}

type route struct {
	Side string
	edgeSegment
}

func readRoutes(slot string) []route {
	data, _ := os.ReadFile(edgePath("desktop-edges.txt"))
	var routes []route
	for _, line := range strings.Split(string(data), "\n") {
		var prefix, key string
		var r route
		if n, _ := fmt.Sscan(line, &prefix, &key, &r.Side, &r.Start, &r.End); n == 5 && prefix == "route" && key == slot {
			routes = append(routes, r)
		}
	}
	return routes
}
func follows(dir, id string) bool {
	data, _ := os.ReadFile(filepath.Join(dir, "peer-routes.txt"))
	return strings.HasPrefix(string(data), "owner "+id+"\n")
}
func storeRoutes(dir, id, slot string, routes []route) error {
	var data strings.Builder
	if id != "" {
		fmt.Fprintf(&data, "owner %s\n", id)
	}
	for _, r := range routes {
		fmt.Fprintf(&data, "%s %s %d %d\n", slot, r.Side, r.Start, r.End)
	}
	path := filepath.Join(dir, "peer-routes.txt")
	f, e := os.CreateTemp(dir, ".routes-*")
	if e != nil {
		return e
	}
	defer os.Remove(f.Name())
	_, e = f.WriteString(data.String())
	c := f.Close()
	if e != nil {
		return e
	}
	if c != nil {
		return c
	}
	return os.Rename(f.Name(), path)
}
