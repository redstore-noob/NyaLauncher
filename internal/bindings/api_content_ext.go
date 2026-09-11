package bindings

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// ToggleContentEntry 启用/禁用内容条目（对应 C# ContentEntryItem 的启停语义：
// 目录内加/去 .disabled 后缀 rename）。entryPath 必须是已存在的文件；
// disable=true 追加 .disabled，disable=false 去掉 .disabled。
func (c *ContentAPI) ToggleContentEntry(entryPath string, disable bool) error {
	if strings.TrimSpace(entryPath) == "" {
		return fmt.Errorf("内容路径为空")
	}
	abs, err := filepath.Abs(entryPath)
	if err != nil {
		return fmt.Errorf("路径无效: %w", err)
	}
	if _, err := os.Stat(abs); err != nil {
		return fmt.Errorf("内容文件不存在: %w", err)
	}
	lower := strings.ToLower(abs)
	var target string
	if disable {
		if strings.HasSuffix(lower, ".disabled") {
			return nil // 已是禁用态，幂等
		}
		target = abs + ".disabled"
	} else {
		if !strings.HasSuffix(lower, ".disabled") {
			return nil // 已是启用态，幂等
		}
		target = abs[:len(abs)-len(".disabled")]
	}
	if err := os.Rename(abs, target); err != nil {
		return fmt.Errorf("重命名失败: %w", err)
	}
	return nil
}
