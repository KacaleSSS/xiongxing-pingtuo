package main

import (
	"bufio"
	"crypto/rand"
	"crypto/tls"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"os"

	"path/filepath"
	"runtime"
	"strings"
	"time"
)

const port = 4243
const protocol = "EdgeMousePair/1"

var opposite = map[string]string{"left": "right", "right": "left", "top": "bottom", "bottom": "top"}

type wire struct {
	Follows  bool
	Routes   []route
	Segment  *edgeSegment
	Platform string
	Edge     string
	Side     string
	Action   string
	Name     string
	Error    string
}

func tlsOptions(cert tls.Certificate, pin string, server bool) *tls.Config {
	cfg := &tls.Config{Certificates: []tls.Certificate{cert}, MinVersion: tls.VersionTLS13, NextProtos: []string{"edgemouse-pair-1"}}
	if server {
		cfg.ClientAuth = tls.RequireAnyClientCert
	} else {
		cfg.InsecureSkipVerify = true
	} // Exact leaf pin is checked below, including on the server.
	cfg.VerifyConnection = func(s tls.ConnectionState) error {
		if s.NegotiatedProtocol != "edgemouse-pair-1" || len(s.PeerCertificates) == 0 || (!(server && pin == "") && fingerprint(s.PeerCertificates[0].Raw) != pin) {
			return errors.New("设备证书不匹配")
		}
		return nil
	}
	return cfg
}
func safeName(s string) string {
	s = strings.Map(func(r rune) rune {
		if r < 32 || r == 127 {
			return -1
		}
		return r
	}, s)
	r := []rune(s)
	if len(r) > 60 {
		r = r[:60]
	}
	return string(r)
}
func (m machine) serveOne(c net.Conn, pin, name string) error {
	return m.serveApproved(c, pin, name, approvePeer)
}
func codeFor(a, b string) string {
	return fingerprint([]byte("EdgeMouse/Windows/Mac/" + a + "/" + b))[:12]
}
func (m machine) serveApproved(c net.Conn, pin, name string, approve func(string, string) bool) error {
	defer c.Close()
	c.SetDeadline(time.Now().Add(10 * time.Second))
	secured := tls.Server(c, tlsOptions(m.cert, pin, true))
	if e := secured.Handshake(); e != nil {
		return e
	}
	var request wire
	if e := json.NewDecoder(io.LimitReader(secured, 2048)).Decode(&request); e != nil {
		return e
	}
	if request.Action == "hello" {
		return json.NewEncoder(secured).Encode(wire{Name: name, Platform: runtime.GOOS, Follows: follows(m.dir, fingerprint(secured.ConnectionState().PeerCertificates[0].Raw))})
	}
	if request.Action != "pair" {
		return errors.New("未知配对请求")
	}
	unknownPolicy := pin == ""
	pin = fingerprint(secured.ConnectionState().PeerCertificates[0].Raw)
	fresh := unknownPolicy && !trusted(m.dir, pin)
	if fresh {
		c.SetDeadline(time.Now().Add(70 * time.Second))
		if !approve(safeName(request.Name), codeFor(pin, fingerprint(m.cert.Certificate[0]))) {
			return json.NewEncoder(secured).Encode(wire{Error: "对方电脑未允许这次连接"})
		}
	}
	ip := c.RemoteAddr().(*net.TCPAddr).IP
	if request.Side != "" {
		m.preferred = opposite[request.Side]
		if m.preferred == "" {
			return errors.New("未知屏幕方向")
		}
	}
	allowReplace := fresh && !(runtime.GOOS == "windows" && m.dir == filepath.Join(os.Getenv("LOCALAPPDATA"), "lan-mouse") && os.Getenv("EDGE_MOUSE_TEST_ENGINE") == "")
	if request.Segment != nil && (!request.Segment.valid() || opposite[request.Side] == "") {
		return errors.New("无效的返回区段")
	}
	for _, r := range request.Routes {
		if opposite[r.Side] == "" || !r.edgeSegment.valid() {
			return errors.New("无效的相邻边缘")
		}
	}
	p, e := m.prepareReplacement(pin, ip, allowReplace)
	if e == nil {
		e = m.install(p, pin, ip)
	}
	if e == nil && request.Segment != nil {
		e = saveReturnEdge(m.dir, p.side, *request.Segment)
	}
	if e == nil && request.Routes != nil {
		e = storeRoutes(m.dir, pin, p.side, request.Routes)
	}
	response := wire{Routes: request.Routes, Action: "paired", Side: p.side, Segment: request.Segment}
	if e != nil {
		response = wire{Error: e.Error()}
	} else {
		if runtime.GOOS == "darwin" {
			emit(event{Kind: "paired", Name: safeName(request.Name)})
		}
		if runtime.GOOS == "windows" && m.engine != "" && os.Getenv("EDGE_MOUSE_TEST_ENGINE") == "" && m.dir == filepath.Join(os.Getenv("LOCALAPPDATA"), "lan-mouse") {
			_ = rememberRole(pin, "windows")
			emit(event{Kind: "incoming", ID: pin, Name: safeName(request.Name), Edge: opposite[request.Edge], Message: p.side})
		}
	}
	return json.NewEncoder(secured).Encode(response)
}
func (m machine) request(addr, id, action string) (wire, error) {
	var peer wire
	dial := &net.Dialer{Timeout: 2 * time.Second}
	conn, e := tls.DialWithDialer(dial, "tcp4", addr, tlsOptions(m.cert, id, false))
	if e != nil {
		return peer, e
	}
	defer conn.Close()
	conn.SetDeadline(time.Now().Add(9 * time.Second))
	hostname, _ := os.Hostname()
	if action == "pair" {
		conn.SetDeadline(time.Now().Add(75 * time.Second))
		emit(event{Kind: "status", Message: "请在对方电脑核对配对码 " + codeFor(fingerprint(m.cert.Certificate[0]), id) + " 并允许连接"})
	}
	if e = json.NewEncoder(conn).Encode(wire{Routes: m.routes, Segment: m.segment, Action: action, Name: hostname, Edge: m.geometric, Side: m.preferred, Platform: runtime.GOOS}); e != nil {
		return peer, e
	}
	e = json.NewDecoder(io.LimitReader(conn, 2048)).Decode(&peer)
	if e == nil && peer.Error != "" {
		e = errors.New(peer.Error)
	}
	return peer, e
}
func (m machine) pair(addr *net.TCPAddr, id string) error {
	ip := addr.IP
	p, e := m.prepare(id, ip)
	if e != nil {
		return e
	}
	peer, e := m.request(addr.String(), id, "pair")
	if e != nil {
		return e
	}
	if peer.Action != "paired" {
		return errors.New("对端未确认配对")
	}
	if m.preferred != "" && peer.Side != opposite[m.preferred] {
		return errors.New("请更新并重新打开 Mac 端以同步布局")
	}
	if len(m.routes) > 0 && len(peer.Routes) != len(m.routes) {
		return errors.New("请更新对方的雄兴屏拓以同步布局")
	}
	if m.segment != nil && (peer.Segment == nil || *peer.Segment != *m.segment) {
		return errors.New("请更新 Mac 应用以启用贴合段返回")
	}
	if e = m.install(p, id, ip); e != nil {
		return e
	}
	if follows(m.dir, id) {
		return storeRoutes(m.dir, "", "", nil)
	}
	return nil
}
func macServer(m machine, pin string) error {
	listener, e := net.Listen("tcp4", ":4243")
	if e != nil {
		return e
	}
	defer listener.Close()
	udp, e := net.ListenUDP("udp4", &net.UDPAddr{Port: port})
	if e != nil {
		return e
	}
	defer udp.Close()
	id := fingerprint(m.cert.Certificate[0])
	name, _ := os.Hostname()
	name = safeName(name)
	if runtime.GOOS == "darwin" {
		emit(event{Kind: "ready"})
	}
	go answerDiscovery(udp, pin, id)
	for {
		c, e := listener.Accept()
		if e != nil {
			return e
		}
		// Serialize the short authenticated config transaction and cap stalled TLS handshakes.
		_ = m.serveOne(c, pin, name)
	}
}

type found struct {
	IP string
	ID string
}

func discoverOnce(localID string, output chan<- found) {
	interfaces, _ := net.Interfaces()
	for _, nic := range interfaces {
		if nic.Flags&net.FlagUp == 0 || nic.Flags&net.FlagBroadcast == 0 || nic.Flags&net.FlagLoopback != 0 {
			continue
		}
		addrs, _ := nic.Addrs()
		for _, addr := range addrs {
			subnet, ok := addr.(*net.IPNet)
			if !ok {
				continue
			}
			ip := subnet.IP.To4()
			ones, bits := subnet.Mask.Size()
			if ip == nil || bits != 32 || ones < 1 || ones > 30 {
				continue
			}
			dest := append(net.IP(nil), ip...)
			for i := range dest {
				dest[i] |= ^subnet.Mask[i]
			}
			go probe(ip, &net.UDPAddr{IP: dest, Port: port}, localID, output)
		}
	}
}

type event struct{ Edge, Kind, IP, ID, Name, Message string }

func emit(ev event) { _ = json.NewEncoder(os.Stdout).Encode(ev) }
func windowsScan(m machine, closed chan<- struct{}) {
	localID := fingerprint(m.cert.Certificate[0])
	discoveries := make(chan found, 16)
	commands := make(chan string, 1)
	go func() {
		s := bufio.NewScanner(os.Stdin)
		for s.Scan() {
			var response event
			if json.Unmarshal(s.Bytes(), &response) == nil && handleApproval(response) {
				continue
			}
			commands <- s.Text()
		}
		close(closed)
		close(commands)
	}()
	tick := time.NewTicker(3 * time.Second)
	defer tick.Stop()
	type seen struct {
		name         string
		platform     string
		last         time.Time
		restored     bool
		followsLocal bool
	}
	peers := map[found]seen{}
	emit(event{Kind: "status", Message: "正在同一局域网查找 Mac / Windows…"})
	if err := m.applyLocalLayout(); err != nil {
		emit(event{Kind: "error", Message: "启动时同步本机引擎：" + err.Error()})
	}
	discoverOnce(localID, discoveries)
	connect := func(p found) {
		state := peers[p]
		state.restored = true
		peers[p] = state
		m.geometric, m.preferred = layoutDock(state.platform)
		m.segment = nil

		if state.platform == "darwin" {
			if m.preferred == "" {
				m.preferred = "left"
				m.geometric = "left"
			}
			var e error
			m.segment, e = returnSegment(m.preferred)
			if e != nil {
				emit(event{Kind: "error", Message: e.Error()})
				return
			}
		}
		m.routes = readRoutes(m.preferred)
		if e := m.pair(&net.TCPAddr{IP: net.ParseIP(p.IP), Port: port}, p.ID); e != nil {
			emit(event{Kind: "error", Message: "配对未完成：" + e.Error()})
			return
		}
		if err := rememberRole(p.ID, state.platform); err != nil {
			emit(event{Kind: "error", Message: err.Error()})
			return
		}
		emit(event{Kind: "paired", IP: p.IP, ID: p.ID, Name: state.name, Message: "双向配置已启用"})
	}
	for {
		select {
		case <-tick.C:
			for p, s := range peers {
				if time.Since(s.last) > 15*time.Second {
					delete(peers, p)
					emit(event{Kind: "lost", IP: p.IP, ID: p.ID})
				}
			}
			discoverOnce(localID, discoveries)
		case p := <-discoveries:
			if p.ID == localID {
				continue
			}
			state, exists := peers[p]
			if !exists {
				remote, e := m.request(net.JoinHostPort(p.IP, fmt.Sprint(port)), p.ID, "hello")
				if e != nil || remote.Name == "" {
					continue
				}
				state.followsLocal = remote.Follows
				state.platform = remote.Platform
				if state.platform != "windows" {
					state.platform = "darwin"
				}
				state.name = safeName(remote.Name) + " (" + map[string]string{"windows": "Windows", "darwin": "Mac"}[state.platform] + ")"
				emit(event{Kind: "found", IP: p.IP, ID: p.ID, Name: state.name})
			}
			state.last = time.Now()
			peers[p] = state
			if !state.restored && trusted(m.dir, p.ID) && (state.platform != "windows" || state.followsLocal || (!follows(m.dir, p.ID) && localID < p.ID)) {
				connect(p)
			}
		case line, open := <-commands:
			if !open {
				return
			}
			var cmd event
			if json.Unmarshal([]byte(line), &cmd) != nil {
				continue
			}
			if cmd.Kind == "layout" {
				if err := m.applyLocalLayout(); err != nil {
					emit(event{Kind: "error", Message: "本机引擎：" + err.Error()})
					continue
				}
				emit(event{Kind: "status", Message: "本机键鼠布局已核对；离线设备重连时同步"})
				for peer := range peers {
					if trusted(m.dir, peer.ID) {
						connect(peer)
					}
				}
				continue
			}
			p := found{cmd.IP, cmd.ID}
			if state, ok := peers[p]; cmd.Kind == "pair" && ok && time.Since(state.last) < 15*time.Second {
				connect(p)
			} else {
				emit(event{Kind: "error", Message: "设备已离线，请等待重新发现"})
			}
		}
	}
}

func answerDiscovery(udp *net.UDPConn, pin, id string) {
	var packet [256]byte
	for {
		n, addr, err := udp.ReadFromUDP(packet[:])
		if err != nil {
			return
		}
		fields := strings.Fields(string(packet[:n]))
		if len(fields) != 4 || fields[0] != protocol || fields[1] != "find" || (!validID(fields[2]) || (pin != "" && fields[2] != pin)) || len(fields[3]) != 24 {
			continue
		}
		reply := protocol + " mac " + fields[3] + " " + id
		udp.WriteToUDP([]byte(reply), addr)
	}
}
func probe(ip net.IP, dest *net.UDPAddr, localID string, output chan<- found) {
	c, e := net.ListenUDP("udp4", &net.UDPAddr{IP: ip})
	if e != nil {
		return
	}
	defer c.Close()
	var token [12]byte
	if _, e = rand.Read(token[:]); e != nil {
		return
	}
	nonce := hex.EncodeToString(token[:])
	_, e = c.WriteToUDP([]byte(protocol+" find "+localID+" "+nonce), dest)
	if e != nil {
		return
	}
	c.SetReadDeadline(time.Now().Add(1600 * time.Millisecond))
	var packet [256]byte
	for {
		n, addr, e := c.ReadFromUDP(packet[:])
		if e != nil {
			return
		}
		parts := strings.Fields(string(packet[:n]))
		if len(parts) == 4 && parts[0] == protocol && parts[1] == "mac" && parts[2] == nonce && validID(parts[3]) && addr.Port == dest.Port {
			select {
			case output <- found{addr.IP.String(), parts[3]}:
			default:
			}
		}
	}
}
