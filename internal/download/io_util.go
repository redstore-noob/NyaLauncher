package download

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"strings"

	"nyalauncher/internal/logs"
)

// httpGetString GET 文本（专用客户端），非 2xx 报 httpStatusError。
func httpGetString(ctx context.Context, client *http.Client, endpoint string) (string, error) {
	req, err := http.NewRequestWithContext(ctx, "GET", endpoint, nil)
	if err != nil {
		return "", err
	}
	resp, err := client.Do(req)
	if err != nil {
		return "", err
	}
	return readAllString(resp)
}

// jsonUnmarshalStrict JSON 解析（独立封装便于统一错误信息）。
func jsonUnmarshalStrict(data []byte, target any) error {
	if err := json.Unmarshal(data, target); err != nil {
		return fmt.Errorf("JSON 解析失败：%w", err)
	}
	return nil
}

// logsWrite 写一条日志（失败不影响主流程）。
func logsWrite(message string) {
	_ = logs.Write("DEBUG", message)
}

// readAllString 读取响应体为字符串；非 2xx 状态码按 httpStatusError 处理
// （对应 C# GetStringAsync 的 EnsureSuccess 语义）。
func readAllString(resp *http.Response) (string, error) {
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return "", &httpStatusError{StatusCode: resp.StatusCode}
	}
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return "", err
	}
	return string(body), nil
}

// readAllBytes 读取响应体为字节；非 2xx 状态码按 httpStatusError 处理。
func readAllBytes(resp *http.Response) ([]byte, error) {
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, &httpStatusError{StatusCode: resp.StatusCode}
	}
	return io.ReadAll(resp.Body)
}

// httpStatusError 非 2xx 响应错误（对应 C# HttpRequestException.StatusCode）。
type httpStatusError struct {
	StatusCode int
}

func (e *httpStatusError) Error() string {
	return "HTTP " + http.StatusText(e.StatusCode)
}

// isHTTPStatusError 判断错误是否为非 2xx 状态错误。
func isHTTPStatusError(err error) bool {
	var statusErr *httpStatusError
	return errors.As(err, &statusErr)
}

// tryDeleteFile 尽力删除文件，失败不影响主流程。
func tryDeleteFile(path string) {
	_ = os.Remove(path)
}

// sanitizeSegment 替换文件名中的非法字符（跨平台基础集，对应 C# Path.GetInvalidFileNameChars 的常见子集）。
func sanitizeSegment(name string) string {
	replacer := strings.NewReplacer(
		"<", "_", ">", "_", ":", "_", "\"", "_", "/", "_", "\\", "_",
		"|", "_", "?", "_", "*", "_",
	)
	return replacer.Replace(name)
}

// containsInvalidFileNameChars 版本 ID 等是否包含文件系统非法字符。
func containsInvalidFileNameChars(s string) bool {
	return strings.ContainsAny(s, "<>:\"/\\|?*")
}
