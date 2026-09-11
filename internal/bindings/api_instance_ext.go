package bindings

// InstanceAPI 扩展：删除实例。与 api_instance.go 分离存放避免冲突。

import (
	"errors"
	"os"
	"path/filepath"
	"strings"
)

// errEmptyPath 路径为空。
var errEmptyPath = errors.New("path is empty")

// errInvalidInstancePath 路径校验失败（不在 versions/ 下或含非法片段）。
var errInvalidInstancePath = errors.New("instance path is outside the game versions directory")

// DeleteInstance 删除实例：即删除 <gameDirectory>/versions/<instanceID> 目录。
// internal/instance、internal/launch 无现成的实例删除入口（launch.TryDeleteDirectory
// 是无校验的尽力删除，不适合直接暴露），故在此实现并先做路径校验防误删：
// instanceID 不得含路径分隔符，且最终路径必须严格位于 versions/ 目录内。
func (a *InstanceAPI) DeleteInstance(instanceID, gameDirectory string) error {
	if instanceID == "" || gameDirectory == "" {
		return errEmptyPath
	}
	if strings.ContainsAny(instanceID, "/\\") || instanceID == "." || instanceID == ".." {
		return errInvalidInstancePath
	}
	versionsDir := filepath.Join(gameDirectory, "versions")
	target := filepath.Join(versionsDir, instanceID)

	// 双重校验：target 与 versionsDir 都取绝对+Clean 后，target 必须在 versionsDir 内。
	absVersions, err := filepath.Abs(versionsDir)
	if err != nil {
		return err
	}
	absTarget, err := filepath.Abs(target)
	if err != nil {
		return err
	}
	rel, err := filepath.Rel(absVersions, absTarget)
	if err != nil || rel == "." || strings.HasPrefix(rel, "..") {
		return errInvalidInstancePath
	}
	if info, err := os.Stat(absTarget); err != nil || !info.IsDir() {
		return os.ErrNotExist
	}
	return os.RemoveAll(absTarget)
}
