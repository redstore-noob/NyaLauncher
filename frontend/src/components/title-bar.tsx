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
import React, { useState, useEffect } from "react";

interface TitleBarProps {
  title?: string;
  className?: string;
}

export const TitleBar: React.FC<TitleBarProps> = ({
                                                    title = "",
                                                    className = ""
                                                  }) => {
  const [isMax, setIsMax] = useState(false);

  //窗口控制函数
  const minimize = () => {
    if (window.runtime?.WindowMinimise) {
      window.runtime.WindowMinimise();
    }
  };

  const maximize = async () => {
    if (window.runtime?.WindowToggleMaximise) {
      await window.runtime.WindowToggleMaximise();
      //更新状态
      if (window.runtime?.WindowIsMaximised) {
        const max = await window.runtime.WindowIsMaximised();
        setIsMax(max);
      }
    }
  };

  const quit = () => {
    if (window.runtime?.Quit) {
      window.runtime.Quit();
    }
  };

  //检查初始最大化状态
  useEffect(() => {
    const checkMaximized = async () => {
      if (window.runtime?.WindowIsMaximised) {
        const max = await window.runtime.WindowIsMaximised();
        setIsMax(max);
      }
    };
    checkMaximized();
  }, []);

  return (
    <div
      className={`
        h-10 
        backdrop-blur-md 
        fixed top-0 left-0 right-0 
        z-50 
        flex items-center 
        px-4 
        select-none
        ${className}
      `}
      style={{ '--wails-draggable': 'drag' } as React.CSSProperties}
    >

      <div className="flex items-center gap-2">
        <span className="text-xl"></span>
        <span className="text-sm font-medium text-gray-700 dark:text-gray-300">
          {title}
        </span>
      </div>

      {/*可拖拽区域*/}
      <div className="flex-1 h-full" />

      {/*窗口控制按钮*/}
      <div
        className="flex items-center gap-1"
        style={{ '--wails-draggable': 'no-drag' } as React.CSSProperties}
      >
        <div
          className="flex items-center gap-2"
          style={{ '--wails-draggable': 'no-drag' } as React.CSSProperties}
        >
          {/*最小化*/}
          <button
            onClick={minimize}
            className="w-8 h-8 flex items-center justify-center hover:bg-gray-200 dark:hover:bg-gray-700 rounded transition-colors"
            title="最小化"
          >
            <svg width="10" height="10" viewBox="0 0 10 10" fill="none">
              <path d="M0 5H10" stroke="currentColor" strokeWidth="1.2" />
            </svg>
          </button>

          {/*最大化/还原*/}
          <button
            onClick={maximize}
            className="w-8 h-8 flex items-center justify-center hover:bg-gray-200 dark:hover:bg-gray-700 rounded transition-colors"
            title={isMax ? "还原" : "最大化"}
          >
            {isMax ? (
              <svg width="10" height="10" viewBox="0 0 10 10" fill="none">
                <rect x="1.5" y="1.5" width="7" height="7" stroke="currentColor" strokeWidth="1.2" fill="none" />
                <rect x="3.5" y="3.5" width="5" height="5" stroke="currentColor" strokeWidth="1.2" fill="none" />
              </svg>
            ) : (
              <svg width="10" height="10" viewBox="0 0 10 10" fill="none">
                <rect x="1" y="1" width="8" height="8" stroke="currentColor" strokeWidth="1.2" fill="none" />
              </svg>
            )}
          </button>

          {/*关闭*/}
          <button
            onClick={quit}
            className="w-8 h-8 flex items-center justify-center hover:bg-red-600 hover:text-white rounded transition-colors"
            title="关闭"
          >
            <svg width="10" height="10" viewBox="0 0 10 10" fill="none">
              <path d="M1 1L9 9M1 9L9 1" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" />
            </svg>
          </button>
        </div>
      </div>
    </div>
  );
};