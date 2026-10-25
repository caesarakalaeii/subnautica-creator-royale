package caster

import (
	"crypto/sha256"
	"crypto/subtle"
	"encoding/hex"
)

// credential is one of a booth's three tokens, held only as its digest so a minted token exists in
// memory no longer than it takes to write the reply. SHA-256 suffices: tokens are 256 random bits.
type credential struct {
	digest [sha256.Size]byte

	// set is whether there is a token at all. An unset credential matches nothing, not even the
	// empty string, so an unconfigured token refuses every client.
	set bool
}

// hashToken is the credential a token stands for. An empty token is an unset credential.
func hashToken(token string) credential {
	if token == "" {
		return credential{}
	}

	return credential{digest: sha256.Sum256([]byte(token)), set: true}
}

// storedCredential is a credential read back from its hex form in the database. An undecodable
// digest is an unset credential, which refuses every token rather than failing startup.
func storedCredential(hexDigest string) credential {
	raw, err := hex.DecodeString(hexDigest)
	if err != nil || len(raw) != sha256.Size {
		return credential{}
	}

	var held credential
	copy(held.digest[:], raw)
	held.set = true

	return held
}

// stored is how this credential is written down: the digest in hex.
func (c credential) stored() string {
	return hex.EncodeToString(c.digest[:])
}

// matches is whether a presented token is this credential, compared in constant time over
// fixed-length digests so a guess leaks nothing about how close it was.
func (c credential) matches(presented string) bool {
	if !c.set || presented == "" {
		return false
	}

	against := sha256.Sum256([]byte(presented))

	return subtle.ConstantTimeCompare(c.digest[:], against[:]) == 1
}
