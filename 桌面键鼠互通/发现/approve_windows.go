package main

import (
	"os/exec"
	"syscall"
)

func approvePeer(name, code string) bool { return waitApproval(name, code) }
func engineCommand(path string, args ...string) *exec.Cmd {
	c := exec.Command(path, args...)
	c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
	return c
}

// windowsScan already owns the frontend pipe on Windows.
func frontendClosed() <-chan struct{} { return nil }
