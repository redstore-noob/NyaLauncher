package music

import "sync"

// ConfigStore 配置存取接口。
// C# 侧依赖 LauncherConfig（NyaLauncher.Core.Config）持久化键值对；
// Go 侧 internal/config 尚未就绪，这里抽象为接口，由调用方注入持久化实现。
type ConfigStore interface {
	// GetValue 读取配置值；不存在时返回空字符串。
	GetValue(key string) string
	// SetValue 写入配置值。
	SetValue(key, value string)
}

// MemoryConfigStore 默认的进程内配置实现（不持久化，重启丢失）。
type MemoryConfigStore struct {
	mu    sync.RWMutex
	value map[string]string
}

// NewMemoryConfigStore 创建内存配置存储。
func NewMemoryConfigStore() *MemoryConfigStore {
	return &MemoryConfigStore{value: make(map[string]string)}
}

// GetValue 读取配置值。
func (s *MemoryConfigStore) GetValue(key string) string {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.value[key]
}

// SetValue 写入配置值。
func (s *MemoryConfigStore) SetValue(key, value string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.value == nil {
		s.value = make(map[string]string)
	}
	s.value[key] = value
}
