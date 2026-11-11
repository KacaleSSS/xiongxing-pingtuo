package main

import (
	"crypto/rand"
	"encoding/hex"
	"sync"
	"time"
)

// Only the application's inherited stdin can approve the request shown in its window.
var pendingApprovals sync.Map

func handleApproval(reply event) bool {
	if reply.Kind != "allow" && reply.Kind != "deny" {
		return false
	}
	if ch, ok := pendingApprovals.Load(reply.ID); ok {
		select {
		case ch.(chan bool) <- reply.Kind == "allow":
		default:
		}
	}
	return true
}
func waitApproval(name, code string) bool {
	var nonce [16]byte
	if _, err := rand.Read(nonce[:]); err != nil {
		return false
	}
	id := hex.EncodeToString(nonce[:])
	ch := make(chan bool, 1)
	pendingApprovals.Store(id, ch)
	defer pendingApprovals.Delete(id)
	emit(event{Kind: "approve", ID: id, Name: name, Message: code})
	timer := time.NewTimer(55 * time.Second)
	defer timer.Stop()
	select {
	case allowed := <-ch:
		return allowed
	case <-timer.C:
		return false
	}
}
