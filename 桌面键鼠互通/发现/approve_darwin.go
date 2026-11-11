package main

import (
	"bufio"
	"encoding/json"
	"os"
	"os/exec"
	"sync"
)

var reader sync.Once
var closed = make(chan struct{})

func frontendClosed() <-chan struct{} {
	reader.Do(func() {
		go func() {
			defer close(closed)
			s := bufio.NewScanner(os.Stdin)
			for s.Scan() {
				var reply event
				if json.Unmarshal(s.Bytes(), &reply) == nil {
					handleApproval(reply)
				}
			}
		}()
	})
	return closed
}
func approvePeer(name, code string) bool                  { return waitApproval(name, code) }
func engineCommand(path string, args ...string) *exec.Cmd { return exec.Command(path, args...) }
