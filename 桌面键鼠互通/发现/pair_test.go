package main

import (
	"bytes"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"fmt"
	"math/big"
	"net"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// Only Lan Mouse's local IPC query is a test double. The network handshake,
// pinning, config files and retry paths below run through the production code.
func TestMain(m *testing.M) {
	if len(os.Args) == 3 && os.Args[1] == "cli" && os.Args[2] == "list" {
		exe, _ := os.Executable()
		cfg, _, err := readConfig(filepath.Join(filepath.Dir(exe), "config.toml"))
		if err != nil {
			os.Exit(1)
		}
		cs, _ := cfg["clients"].([]any)
		for i, v := range cs {
			c := v.(map[string]any)
			fmt.Printf("id %d: %v:4242 (%v) active: %v, ips: %v\n", i, c["hostname"], c["position"], c["activate_on_startup"], c["ips"])
		}
		os.Exit(0)
	}
	os.Exit(m.Run())
}
func fixture(t *testing.T, side string) machine {
	t.Helper()
	key, e := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if e != nil {
		t.Fatal(e)
	}
	tpl := &x509.Certificate{SerialNumber: big.NewInt(1), NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().Add(time.Hour), KeyUsage: x509.KeyUsageDigitalSignature}
	der, e := x509.CreateCertificate(rand.Reader, tpl, tpl, &key.PublicKey, key)
	if e != nil {
		t.Fatal(e)
	}
	dir := t.TempDir()
	exe, _ := os.Executable()
	engine := filepath.Join(dir, filepath.Base(exe))
	if e = os.Link(exe, engine); e != nil {
		data, e := os.ReadFile(exe)
		if e != nil {
			t.Fatal(e)
		}
		if e = os.WriteFile(engine, data, 0700); e != nil {
			t.Fatal(e)
		}
	}
	return machine{dir: dir, engine: engine, side: side, cert: tls.Certificate{Certificate: [][]byte{der}, PrivateKey: key}}
}
func TestPairing(t *testing.T) {
	win, mac := fixture(t, "left"), fixture(t, "right")
	winID, macID := fingerprint(win.cert.Certificate[0]), fingerprint(mac.cert.Certificate[0])
	listener, e := net.ListenTCP("tcp4", &net.TCPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if e != nil {
		t.Fatal(e)
	}
	defer listener.Close()
	go func() {
		for {
			c, e := listener.Accept()
			if e != nil {
				return
			}
			mac.serveOne(c, winID, "test mac")
		}
	}()
	addr := listener.Addr().(*net.TCPAddr)
	if reply, e := win.request(addr.String(), macID, "hello"); e != nil || reply.Name != "test mac" {
		t.Fatal(reply, e)
	}
	if trusted(mac.dir, winID) || trusted(win.dir, macID) {
		t.Fatal("discovery authorized input")
	}
	alien := fixture(t, "left")
	if e = alien.pair(addr, macID); e == nil {
		t.Fatal("foreign Windows accepted")
	}
	if _, e = win.request(addr.String(), strings.Repeat("0", 64), "hello"); e == nil {
		t.Fatal("spoofed Mac accepted")
	}
	if e = win.pair(addr, macID); e != nil {
		t.Fatal(e)
	}
	if !trusted(win.dir, macID) || !trusted(mac.dir, winID) {
		t.Fatal("missing authorization")
	}
	_, first, _ := readConfig(filepath.Join(win.dir, "config.toml"))
	if e = win.pair(addr, macID); e != nil {
		t.Fatal(e)
	}
	_, second, _ := readConfig(filepath.Join(win.dir, "config.toml"))
	if !bytes.Equal(first, second) {
		t.Fatal("duplicate pair changed config")
	}
}
func TestMigratePreviousMacSetup(t *testing.T) {
	m := fixture(t, "right")
	id := strings.Repeat("a", 64)
	cfg := fmt.Sprintf("port=4242\n[authorized_fingerprints]\n%q='Windows'\n[[clients]]\nips=['10.1.1.10']\nposition='right'\nactivate_on_startup=true\n", colon(id))
	path := filepath.Join(m.dir, "config.toml")
	os.WriteFile(path, []byte(cfg), 0600)
	p, e := m.prepare(id, net.ParseIP("10.1.1.20"))
	if e != nil {
		t.Fatal(e)
	}
	if e = p.apply(); e != nil {
		t.Fatal(e)
	}
	d, _, _ := readConfig(path)
	if len(d["clients"].([]any)) != 1 || d["port"] != int64(4242) {
		t.Fatal("legacy config lost")
	}
	if _, e = m.prepare(strings.Repeat("b", 64), net.ParseIP("10.1.1.40")); e == nil {
		t.Fatal("occupied edge overwritten")
	}
}
func TestUDPDiscovery(t *testing.T) {
	local := net.IPv4(127, 0, 0, 1)
	receiver, e := net.ListenUDP("udp4", &net.UDPAddr{IP: local})
	if e != nil {
		t.Fatal(e)
	}
	defer receiver.Close()
	pin, id := strings.Repeat("1", 64), strings.Repeat("2", 64)
	go answerDiscovery(receiver, pin, id)
	out := make(chan found, 1)
	go probe(local, receiver.LocalAddr().(*net.UDPAddr), pin, out)
	select {
	case p := <-out:
		if p.IP != "127.0.0.1" || p.ID != id {
			t.Fatal(p)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("discovery timed out")
	}
}

// The harness starts an isolated Lan Mouse daemon with dummy input backends.
func TestRealWindowsEngine(t *testing.T) {
	engine := os.Getenv("EDGE_MOUSE_TEST_ENGINE")
	if engine == "" {
		t.Skip("requires an isolated Lan Mouse daemon")
	}
	root := os.Getenv("LOCALAPPDATA")
	if !strings.HasPrefix(filepath.Base(root), "edge-pair-live-") {
		t.Fatal("refusing a non-test config directory")
	}
	win, e := openMachine(engine)
	if e != nil {
		t.Fatal(e)
	}
	mac := fixture(t, "right")
	local := net.IPv4(127, 0, 0, 1)
	l, e := net.ListenTCP("tcp4", &net.TCPAddr{IP: local})
	if e != nil {
		t.Fatal(e)
	}
	defer l.Close()
	go func() {
		for {
			c, e := l.Accept()
			if e != nil {
				return
			}
			mac.serveOne(c, fingerprint(win.cert.Certificate[0]), "Mac fixture")
		}
	}()
	id := fingerprint(mac.cert.Certificate[0])
	if e = win.pair(l.Addr().(*net.TCPAddr), id); e != nil {
		t.Fatal(e)
	}
	if e = win.pair(l.Addr().(*net.TCPAddr), id); e != nil {
		t.Fatal(e)
	}
}
func TestUniversalSwap(t *testing.T) {
	mac, a, b := fixture(t, "right"), fixture(t, "left"), fixture(t, "left")
	mid := fingerprint(mac.cert.Certificate[0])
	run := func(win machine, allow, expectPrompt bool) error {
		wid := fingerprint(win.cert.Certificate[0])
		l, e := net.ListenTCP("tcp4", &net.TCPAddr{IP: net.IPv4(127, 0, 0, 1)})
		if e != nil {
			t.Fatal(e)
		}
		defer l.Close()
		called := make(chan bool, 1)
		go func() {
			c, _ := l.Accept()
			prompt := false
			mac.serveApproved(c, "", "Mac", func(name, code string) bool { prompt = true; return allow && code == codeFor(wid, mid) })
			called <- prompt
		}()
		e = win.pair(l.Addr().(*net.TCPAddr), mid)
		if <-called != expectPrompt {
			t.Fatal("wrong consent flow")
		}
		return e
	}
	if run(a, false, true) == nil || trusted(mac.dir, fingerprint(a.cert.Certificate[0])) {
		t.Fatal("denied machine authorized")
	}
	if e := run(a, true, true); e != nil {
		t.Fatal(e)
	}
	if e := run(a, false, false); e != nil {
		t.Fatal(e)
	}
	if e := run(b, true, true); e != nil {
		t.Fatal(e)
	}
	if trusted(mac.dir, fingerprint(a.cert.Certificate[0])) {
		t.Fatal("previous Windows still trusted")
	}
}
func TestLayoutDirections(t *testing.T) {
	win, mac := fixture(t, "left"), fixture(t, "right")
	id := fingerprint(mac.cert.Certificate[0])
	l, e := net.ListenTCP("tcp4", &net.TCPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if e != nil {
		t.Fatal(e)
	}
	defer l.Close()
	go func() {
		for {
			c, e := l.Accept()
			if e != nil {
				return
			}
			mac.serveOne(c, fingerprint(win.cert.Certificate[0]), "Mac")
		}
	}()
	for _, side := range []string{"left", "top", "right", "bottom"} {
		win.preferred = side
		if e = win.pair(l.Addr().(*net.TCPAddr), id); e != nil {
			t.Fatal(e)
		}
		for _, item := range []struct {
			peer machine
			want string
		}{{win, side}, {mac, opposite[side]}} {
			d, _, err := readConfig(filepath.Join(item.peer.dir, "config.toml"))
			if err != nil {
				t.Fatal(err)
			}
			clients := d["clients"].([]any)
			if len(clients) != 1 || clients[0].(map[string]any)["position"] != item.want {
				t.Fatal(side, clients)
			}
		}
	}
}
func TestIntegratedDesktopLayout(t *testing.T) {
	root := t.TempDir()
	t.Setenv("LOCALAPPDATA", root)
	if e := os.MkdirAll(edgePath(""), 0700); e != nil {
		t.Fatal(e)
	}
	m := fixture(t, "left")
	macID := strings.Repeat("a", 64)
	pcID := strings.Repeat("b", 64)
	for i, id := range []string{macID, pcID} {
		role := []string{"darwin", "windows"}[i]
		m.preferred = []string{"left", "top"}[i]
		p, e := m.prepare(id, net.IPv4(127, 0, 0, byte(i+1)))
		if e != nil {
			t.Fatal(e)
		}
		if e = p.apply(); e != nil {
			t.Fatal(e)
		}
		if e = rememberRole(id, role); e != nil {
			t.Fatal(e)
		}
	}
	// Both tiles touch the top of different monitors: the engine uses two slots.
	if e := os.WriteFile(edgePath("layout.json"), []byte(`{"Mac":{"Side":"top"},"PC":{"Side":"top","Slot":"left"}}`), 0600); e != nil {
		t.Fatal(e)
	}
	if e := m.applyLocalLayout(); e != nil {
		t.Fatal(e)
	}
	d, _, _ := readConfig(filepath.Join(m.dir, "config.toml"))
	cs := d["clients"].([]any)
	if len(cs) != 2 || cs[0].(map[string]any)["position"] != "top" || cs[1].(map[string]any)["position"] != "left" {
		t.Fatal(cs)
	}
	geom, slot := layoutDock("windows")
	if geom != "top" || slot != "left" {
		t.Fatal(geom, slot)
	}
}
func TestSegmentProtocol(t *testing.T) {
	root := t.TempDir()
	t.Setenv("LOCALAPPDATA", root)
	os.MkdirAll(edgePath(""), 0700)
	os.WriteFile(edgePath("desktop-edges.txt"), []byte("left left 1920 -75 4480 1525 325 965\nroute left right 250000 650000\nroute left bottom 0 900000\n"), 0600)
	s, e := returnSegment("left")
	if e != nil || *s != (edgeSegment{250000, 650000}) {
		t.Fatal(s, e)
	}
	mac, win := fixture(t, "right"), fixture(t, "left")
	win.segment = s
	win.routes = readRoutes("left")
	if len(win.routes) != 2 {
		t.Fatal(win.routes)
	}
	win.preferred = "left"
	l, e := net.ListenTCP("tcp4", &net.TCPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if e != nil {
		t.Fatal(e)
	}
	defer l.Close()
	go func() { c, _ := l.Accept(); mac.serveOne(c, fingerprint(win.cert.Certificate[0]), "Mac") }()
	if e = win.pair(l.Addr().(*net.TCPAddr), fingerprint(mac.cert.Certificate[0])); e != nil {
		t.Fatal(e)
	}
	data, e := os.ReadFile(filepath.Join(mac.dir, "return-edge.txt"))
	if e != nil || string(data) != "right 250000 650000\n" {
		t.Fatal(string(data), e)
	}
	routes, e := os.ReadFile(filepath.Join(mac.dir, "peer-routes.txt"))
	expected := "owner " + fingerprint(win.cert.Certificate[0]) + "\nright right 250000 650000\nright bottom 0 900000\n"
	if e != nil || string(routes) != expected {
		t.Fatal(string(routes), e)
	}

}
