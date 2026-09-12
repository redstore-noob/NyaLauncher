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

import React from 'react';
import GameDirectorySection from './settings/GameDirectorySection';
import JavaSection from './settings/JavaSection';
import LaunchSection from './settings/LaunchSection';
import MemorySection from './settings/MemorySection';
import DownloadSection from './settings/DownloadSection';
import AboutSection from './settings/AboutSection';

const SettingsPage: React.FC = () => {
  return (
    <div className="h-full w-full overflow-y-auto">
      <div className="max-w-2xl mx-auto px-6 py-8 space-y-10">
        <h1 className="text-xl font-semibold text-gray-800 dark:text-gray-200">设置</h1>
        <GameDirectorySection />
        <JavaSection />
        <LaunchSection />
        <MemorySection />
        <DownloadSection />
        <AboutSection />
      </div>
    </div>
  );
};

export default SettingsPage;