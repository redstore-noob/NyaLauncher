// 存档的导出、备份与删除操作。移植自 NyaLauncher.Core/Content/GameSaveService.cs。
// 存档在磁盘上是一个目录，导出/备份都会将该目录打包为 .zip（丢弃会话锁文件）。
package content

import (
	"archive/zip"
	"context"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// sessionLockName 导出时丢弃的会话锁文件名。
const sessionLockName = "session.lock"

// ExportSave 将指定存档目录打包到目标 .zip 路径。目标已存在时覆盖。
// 对应 C# ExportAsync（C# 失败返回 null；Go 版改为返回 error，见 PORTING_NOTES.md）。
func ExportSave(ctx context.Context, saveDirectory, destinationZipPath string) (string, error) {
	if strings.TrimSpace(saveDirectory) == "" ||
		strings.TrimSpace(destinationZipPath) == "" {
		return "", fmt.Errorf("参数无效")
	}
	info, err := os.Stat(saveDirectory)
	if err != nil || !info.IsDir() {
		return "", fmt.Errorf("存档目录不存在：%s", saveDirectory)
	}

	temporary := destinationZipPath + ".nya-pack"
	defer func() { tryDeleteFile(temporary) }()

	normalized := trimEndingSep(mustAbsPath(saveDirectory))
	saveName := filepath.Base(normalized)
	parent := filepath.Dir(destinationZipPath)
	if strings.TrimSpace(parent) == "" {
		return "", fmt.Errorf("目标路径无效：%s", destinationZipPath)
	}
	if err := os.MkdirAll(parent, 0o755); err != nil {
		return "", err
	}

	if err := createSaveArchive(ctx, normalized, saveName, temporary); err != nil {
		return "", err
	}
	if err := os.Rename(temporary, destinationZipPath); err != nil {
		return "", err
	}
	// Windows 上 os.Rename 不覆盖已存在文件；失败时改为覆盖式移动
	if _, err := os.Stat(destinationZipPath); err != nil {
		data, readErr := os.ReadFile(temporary)
		if readErr != nil {
			return "", readErr
		}
		if err := os.WriteFile(destinationZipPath, data, 0o644); err != nil {
			return "", err
		}
	}
	return destinationZipPath, nil
}

// BackupSave 在存档同级目录生成 {存档名}-备份-{时间戳}.zip。
func BackupSave(ctx context.Context, saveDirectory string) (string, error) {
	if strings.TrimSpace(saveDirectory) == "" {
		return "", fmt.Errorf("参数无效")
	}
	info, err := os.Stat(saveDirectory)
	if err != nil || !info.IsDir() {
		return "", fmt.Errorf("存档目录不存在：%s", saveDirectory)
	}

	normalized := trimEndingSep(mustAbsPath(saveDirectory))
	parent := filepath.Dir(normalized)
	saveName := filepath.Base(normalized)

	// 毫秒级时间戳：同秒内连续备份两次不应互相覆盖
	stamp := time.Now().Format("20060102-150405.000")
	stamp = strings.Replace(stamp, ".", "", 1)
	destination := filepath.Join(parent, fmt.Sprintf("%s-备份-%s.zip", saveName, stamp))
	return ExportSave(ctx, normalized, destination)
}

// DeleteSave 递归删除存档目录；目录已不存在视为成功。
func DeleteSave(saveDirectory string) error {
	if strings.TrimSpace(saveDirectory) == "" {
		return fmt.Errorf("参数无效")
	}
	normalized := mustAbsPath(saveDirectory)
	if info, err := os.Stat(normalized); err != nil || !info.IsDir() {
		return nil
	}
	return os.RemoveAll(normalized)
}

// createSaveArchive 递归打包存档目录到 archivePath（先写临时文件由调用方改名）。
func createSaveArchive(ctx context.Context, sourceDirectory, rootEntryName, archivePath string) error {
	file, err := os.OpenFile(archivePath, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	defer file.Close()

	archive := zip.NewWriter(file)
	defer archive.Close()

	return filepath.WalkDir(sourceDirectory, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if err := ctx.Err(); err != nil {
			return err
		}
		if entry.IsDir() {
			return nil
		}
		relative, err := filepath.Rel(sourceDirectory, path)
		if err != nil {
			return err
		}
		if strings.EqualFold(filepath.Base(path), sessionLockName) {
			return nil
		}
		// ZIP 规范要求条目名使用 '/'：Windows 分隔符会被非 Windows 工具解包成损坏文件名
		entryName := rootEntryName + "/" + filepath.ToSlash(relative)
		info, err := entry.Info()
		if err != nil {
			return err
		}
		header, err := zip.FileInfoHeader(info)
		if err != nil {
			return err
		}
		header.Name = entryName
		header.Method = zip.Deflate
		entryWriter, err := archive.CreateHeader(header)
		if err != nil {
			return err
		}
		source, err := os.Open(path)
		if err != nil {
			return err
		}
		defer source.Close()
		_, err = io.Copy(entryWriter, source)
		return err
	})
}

// tryDeleteFile 删除文件，失败可忽略：残留的 .nya-pack 可被下次导出覆盖。
func tryDeleteFile(path string) {
	if info, err := os.Stat(path); err == nil && !info.IsDir() {
		_ = os.Remove(path)
	}
}
