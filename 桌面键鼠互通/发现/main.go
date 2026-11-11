package main

import (
	"errors"
	"fmt"
	"os"

	"path/filepath"
	"runtime"
	"strings"
	"time"
)

var ownedEngine *os.Process
var engineDone = make(chan struct{})

func engineReady(engine string) bool { return engineCommand(engine, "cli", "list").Run() == nil }
func openMachine(engine string) (machine, error) {
	m := machine{engine: engine, side: "left"}
	if runtime.GOOS == "windows" {
		m.dir = filepath.Join(os.Getenv("LOCALAPPDATA"), "lan-mouse")
	} else {
		base := os.Getenv("XDG_CONFIG_HOME")
		if base == "" {
			home, e := os.UserHomeDir()
			if e != nil {
				return m, e
			}
			base = filepath.Join(home, ".config")
		}
		m.dir = filepath.Join(base, "lan-mouse")
		m.side = "right"
	}
	existing := engineReady(engine)
	if existing {
		return m, errors.New("已有键鼠服务在运行，请关闭旧版雄兴屏拓后重新打开。")
	}
	if !existing {
		daemon := engineCommand(engine, "daemon")
		if e := daemon.Start(); e != nil {
			return m, e
		}
		ownedEngine = daemon.Process
		go func() { _ = daemon.Wait(); close(engineDone) }()
	}
	for i := 0; i < 35; i++ {
		if engineReady(engine) {
			break
		}
		time.Sleep(250 * time.Millisecond)
	}
	if !engineReady(engine) {
		return m, errors.New("内置输入服务未就绪，请检查系统权限后重新打开雄兴屏拓")
	}
	doc, _, e := readConfig(filepath.Join(m.dir, "config.toml"))
	if e != nil {
		return m, e
	}
	if p, ok := doc["port"]; ok && fmt.Sprint(p) != "4242" {
		return m, errors.New("当前键鼠服务使用自定义端口，此版配对使用 4242")
	}
	path := filepath.Join(m.dir, "lan-mouse.pem")
	if custom, ok := doc["cert_path"].(string); ok {
		path = custom
	}
	m.cert, e = identity(path)
	return m, e
}
func run() error {
	if len(os.Args) < 3 {
		return errors.New("missing engine path")
	}
	mode, engine := os.Args[1], os.Args[2]
	defer func() {
		if ownedEngine != nil {
			_ = ownedEngine.Kill()
			<-engineDone
		}
	}()
	m, e := openMachine(engine)
	if e != nil {
		return e
	}
	switch mode {
	case "windows":
		go func() {
			if err := macServer(m, ""); err != nil {
				emit(event{Kind: "error", Message: "无法接收其他 Windows 的连接：" + err.Error()})
			}
		}()
		closed := make(chan struct{})
		go windowsScan(m, closed)
		<-closed
		return nil
	case "mac":
		done := make(chan error, 1)
		go func() { done <- macServer(m, "") }()
		select {
		case e := <-done:
			return e
		case <-frontendClosed():
			return nil
		case <-engineDone:
			return errors.New("输入服务已退出，请重新打开雄兴屏拓。")
		}
	}
	return errors.New("unknown mode")
}
func main() {
	if e := run(); e != nil {
		emit(event{Kind: "error", Message: strings.TrimSpace(e.Error())})
		fmt.Fprintln(os.Stderr, e)
		os.Exit(1)
	}
}
