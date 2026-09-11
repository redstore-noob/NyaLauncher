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
import React, { useState } from 'react';
import { Button, Tooltip } from '@heroui/react';
import { HomeIcon, SettingsIcon, MenuIcon } from '../icons';

export type PageKey = 'home' | 'settings';

export const NAV_ITEMS: { key: PageKey; label: string; icon: React.ReactNode }[] = [
  { key: 'home', label: '主页', icon: <HomeIcon /> },
  { key: 'settings', label: '设置', icon: <SettingsIcon /> },
];

interface SidebarProps {
  activeKey: PageKey;
  onNavigate: (key: PageKey) => void;
}

const Sidebar: React.FC<SidebarProps> = ({ activeKey, onNavigate }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  return (
    <aside
      className={`
        absolute left-0 top-10 h-[calc(100%-2.5rem)] z-20
        bg-white dark:bg-gray-900
        transition-[width] duration-200 ease-in-out
        flex flex-col overflow-hidden
        ${isExpanded ? 'w-[168px]' : 'w-16'}
      `}
    >
      <div className="h-14 flex items-center flex-shrink-0">
        <div className="w-16 flex items-center justify-center flex-shrink-0">
          <Button
            isIconOnly
            variant="light"
            onPress={() => setIsExpanded((v) => !v)}
            className="text-gray-500 min-w-10 w-10 h-10"
          >
            <MenuIcon />
          </Button>
        </div>
      </div>

      {/*菜单*/}
      <nav className="flex-1 overflow-y-auto py-4">
        <ul className="space-y-1 px-2">
          {NAV_ITEMS.map((item) => {
            const isActive = activeKey === item.key;
            const button = (
              <Button
                isIconOnly={!isExpanded}
                variant={isActive ? 'flat' : 'light'}
                color={isActive ? 'primary' : 'default'}
                onPress={() => onNavigate(item.key)}
                className={`
                  ${isExpanded ? 'w-full justify-start gap-3 px-3' : 'min-w-0 w-10 h-10 mx-auto'}
                `}
              >
                {isExpanded ? (
                  <>
                    <span className="w-5 flex-shrink-0 flex items-center justify-center">
                      {item.icon}
                    </span>
                    <span className="whitespace-nowrap text-sm">{item.label}</span>
                  </>
                ) : (
                  <span className="flex items-center justify-center">{item.icon}</span>
                )}
              </Button>
            );

            return (
              <li key={item.key}>
                {isExpanded ? (
                  button
                ) : (
                  <Tooltip content={item.label} placement="right" delay={200}>
                    {button}
                  </Tooltip>
                )}
              </li>
            );
          })}
        </ul>
      </nav>
    </aside>
  );
};

export default Sidebar;