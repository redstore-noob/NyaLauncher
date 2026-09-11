// Package auth 账号敏感字段（AccessToken/RefreshToken）的保护器。
// Windows 上使用 DPAPI（CurrentUser 范围，crypt32.dll，经 syscall 懒加载调用；
// 选型说明见 PORTING_NOTES.md）；其他平台使用「密钥文件 + AES-GCM」的等价方案，
// 密钥文件以 0600 权限保存在启动器存储目录，靠文件系统权限隔离其他用户。
// 加密格式：nyaenc1: + Base64(密文)。平台不支持时回落明文（宁明文不丢数据）。
package auth

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"encoding/base64"
	"os"
	"path/filepath"
	"strings"
	"sync"

	"nyalauncher/internal/config"
)

// EncryptedPrefix 加密存储值的前缀，与 C# AccountSecretProtector 保持一致。
const EncryptedPrefix = "nyaenc1:"

// additionalEntropy 应用专属附加熵（与 C# 完全相同的 16 字节，
// Windows 上作为 DPAPI entropy；非 Windows 上参与密钥派生）。
// 阻止「裸 DPAPI blob」被直接搬到其他环境解密，属加固而非硬边界。
var additionalEntropy = []byte{
	0x4E, 0x79, 0x61, 0x4C, 0x61, 0x75, 0x6E, 0x63,
	0x68, 0x65, 0x72, 0x2E, 0x41, 0x63, 0x63, 0x6F,
}

// IsAvailable 当前平台是否支持加密。
func IsAvailable() bool { return platformSecretAvailable() }

// Protect 加密文本；成功返回带 EncryptedPrefix 前缀的存储串，
// 平台不支持或加密失败返回空串（调用方应回落明文以不丢数据）。
func Protect(plaintext string) string {
	if !IsAvailable() {
		return ""
	}
	return platformProtect(plaintext)
}

// Unprotect 解密 Protect 产出的存储串；前缀不匹配、平台不支持或解密失败返回空串。
func Unprotect(stored string) string {
	if !IsAvailable() || !strings.HasPrefix(stored, EncryptedPrefix) ||
		len(stored) <= len(EncryptedPrefix) {
		return ""
	}
	return platformUnprotect(stored[len(EncryptedPrefix):])
}

// ---------------------------------------------------------------------------
// 非 Windows（及非 DPAPI）平台：密钥文件 + AES-256-GCM。
// 密钥文件 account.secret.key 保存在启动器存储目录，权限 0600；
// 目录本身按平台默认权限创建，等效于 C# 中「非 Windows 保持明文 + 用户目录隔离」
// 的安全级别并略有加强。
// ---------------------------------------------------------------------------

var (
	aesGate       sync.Mutex
	aesCipherText cipher.AEAD
	aesCipherOnce sync.Once
)

func platformSecretAvailable() bool {
	if runtimeIsWindows() {
		return true
	}
	_, err := loadOrCreateAesKey()
	return err == nil
}

func platformProtect(plaintext string) string {
	if runtimeIsWindows() {
		return dpapiProtect(plaintext)
	}
	aead, err := sharedAesCipher()
	if err != nil {
		return ""
	}
	nonce := make([]byte, aead.NonceSize())
	if _, err := rand.Read(nonce); err != nil {
		return ""
	}
	// 附加熵作为 AAD 绑定应用身份
	sealed := aead.Seal(nil, nonce, []byte(plaintext), additionalEntropy)
	return EncryptedPrefix + base64.StdEncoding.EncodeToString(append(nonce, sealed...))
}

func platformUnprotect(encodedBase64 string) string {
	if runtimeIsWindows() {
		return dpapiUnprotect(encodedBase64)
	}
	raw, err := base64.StdEncoding.DecodeString(encodedBase64)
	if err != nil {
		return ""
	}
	aead, err := sharedAesCipher()
	if err != nil {
		return ""
	}
	nonceSize := aead.NonceSize()
	if len(raw) < nonceSize {
		return ""
	}
	plain, err := aead.Open(nil, raw[:nonceSize], raw[nonceSize:], additionalEntropy)
	if err != nil {
		return ""
	}
	return string(plain)
}

func sharedAesCipher() (cipher.AEAD, error) {
	aesGate.Lock()
	defer aesGate.Unlock()
	aesCipherOnce.Do(func() {
		key, err := loadOrCreateAesKey()
		if err != nil {
			return
		}
		block, blockErr := aes.NewCipher(key)
		if blockErr != nil {
			return
		}
		aesCipherText, _ = cipher.NewGCM(block)
	})
	return aesCipherText, nil
}

// loadOrCreateAesKey 读取或首次生成 AES-256 密钥文件（0600 权限）。
func loadOrCreateAesKey() ([]byte, error) {
	keyPath := filepath.Join(config.DefaultStorageDirectory(), "account.secret.key")
	if data, err := os.ReadFile(keyPath); err == nil {
		decoded, decodeErr := base64.StdEncoding.DecodeString(strings.TrimSpace(string(data)))
		if decodeErr == nil && len(decoded) == 32 {
			return decoded, nil
		}
		return nil, os.ErrInvalid
	}
	key := make([]byte, 32)
	if _, err := rand.Read(key); err != nil {
		return nil, err
	}
	if err := os.MkdirAll(filepath.Dir(keyPath), 0o700); err != nil {
		return nil, err
	}
	encoded := base64.StdEncoding.EncodeToString(key)
	if err := os.WriteFile(keyPath, []byte(encoded), 0o600); err != nil {
		return nil, err
	}
	return key, nil
}
