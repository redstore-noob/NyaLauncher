/*
MIT License

Copyright (c) 2024 Next UI
Copyright (c) 2026 烟花

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
declare interface Window {
    runtime: { //虽然查不到用法但是别动！！！！！！！！！！！！！！！！！！
        //别动！！！！！！！！！！！！！！！！！！！！！！！！！
        //窗口控制要靠这些！！！！！！！！！！！！！！！！！！！！！！！！！！！！！

        // 窗口控制
        WindowReload: () => void;
        WindowSetTitle: (title: string) => void;
        WindowFullscreen: () => void;
        WindowUnfullscreen: () => void;
        WindowIsFullscreen: () => Promise<boolean>;
        WindowCenter: () => void;
        WindowToggleMaximise: () => void;
        WindowMaximise: () => void;
        WindowUnmaximise: () => void;
        WindowIsMaximised: () => Promise<boolean>;
        WindowMinimise: () => void;
        WindowUnminimise: () => void;
        WindowSetSize: (width: number, height: number) => void;
        WindowGetSize: () => Promise<{ w: number; h: number }>;
        WindowSetPosition: (x: number, y: number) => void;
        WindowGetPosition: () => Promise<{ x: number; y: number }>;

        // 应用控制
        Quit: () => void;
        Hide: () => void;
        Show: () => void;

        // 事件系统
        EventsOn: (eventName: string, callback: (...args: any[]) => void) => void;
        EventsOff: (eventName: string, additionalEventNames?: string[]) => void;
        EventsOnce: (eventName: string, callback: (...args: any[]) => void) => void;
        EventsEmit: (eventName: string, ...args: any[]) => void;

        // 其他运行时方法
        Log: (message: string) => void;
        LogDebug: (message: string) => void;
        LogInfo: (message: string) => void;
        LogWarning: (message: string) => void;
        LogError: (message: string) => void;

        // 环境信息
        Environment: () => Promise<{
            platform: string;
            arch: string;
        }>;
    };
}