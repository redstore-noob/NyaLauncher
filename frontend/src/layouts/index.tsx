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
import Sidebar, { PageKey } from './Sidebar';
import HomePage from './home';
import SettingsPage from './settings';
import { TitleBar } from '../components/title-bar.tsx';

const Layouts: React.FC = () => {
  const [activeKey, setActiveKey] = useState<PageKey>('home');

  return (
    <div className="flex h-screen w-screen bg-white dark:bg-gray-950 text-gray-900 dark:text-gray-100 overflow-hidden">
      {/*全局标题栏，也就是窗口标题*/}
      <TitleBar title="NyaLauncher" />

      {/*标题栏下方的内容区*/}
      <div className="flex flex-1 w-full pt-10 relative">
        {/*全局左侧栏*/}
        <Sidebar activeKey={activeKey} onNavigate={setActiveKey} />

        {/*内容区*/}
        <div className="flex-1 ml-16 relative">
          {activeKey === 'home' && <HomePage />}
          {activeKey === 'settings' && <SettingsPage />}
        </div>
      </div>
    </div>
  );
};

export default Layouts;