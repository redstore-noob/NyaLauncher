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
import React, { useEffect, useState } from 'react';
import { Button, Chip } from '@heroui/react';
import Section from './Section';
import {
  GetJavaPaths, AddJava, RemoveJava, SetPrimaryJava,
} from '../../../wailsjs/go/bindings/ConfigAPI';
import {
  DetectJavaMajorVersion,
} from '../../../wailsjs/go/bindings/LauncherAPI';
import {
  GetInstalledJavaRuntimes,
  DeleteJavaRuntime,
} from '../../../wailsjs/go/bindings/DownloadAPI';
import { SelectFile } from '../../../wailsjs/go/bindings/SystemAPI';
import type { config, download } from '../../../wailsjs/go/models';

const JavaSection: React.FC = () => {
  const [paths, setPaths] = useState<config.JavaPathItem[]>([]);
  const [runtimes, setRuntimes] = useState<download.InstalledJavaRuntime[]>([]);

  const reload = async () => {
    const [p, r] = await Promise.all([GetJavaPaths(), GetInstalledJavaRuntimes()]);
    setPaths(p ?? []);
    setRuntimes(r ?? []);
  };

  useEffect(() => {
    reload();
  }, []);

  const add = async () => {
    const picked = await SelectFile('选择java可执行文件', 'Java', 'java.exe;java');
    if (!picked) return;
    const major = await DetectJavaMajorVersion(picked);
    await AddJava(picked, major ? String(major) : 'unknown');
    await reload();
  };

  return (
    <Section title="Java运行时" description="设置启动游戏时优先使用的Java">
      <div className="flex items-center justify-between">
        <div className="text-sm text-gray-800 dark:text-gray-200">已保存的Java</div>
        <Button size="sm" variant="flat" onPress={add}>添加</Button>
      </div>
      {paths.length === 0 ? (
        <div className="text-xs text-gray-400">暂无，如果没有配置将会自动探测</div>
      ) : (
        <ul className="space-y-1">
          {paths.map((item, idx) => (
            <li key={item.JavaPath} className="flex items-center gap-2 text-sm py-1">
              <span className="truncate flex-1 text-gray-700 dark:text-gray-300">
                {item.JavaPath}
              </span>
              <Chip size="sm" variant="flat">{item.JavaVersion}</Chip>
              {idx === 0 ? (
                <Chip size="sm" color="primary" variant="flat">默认</Chip>
              ) : (
                <Button
                  size="sm"
                  variant="light"
                  onPress={async () => {
                    await SetPrimaryJava(item.JavaPath);
                    await reload();
                  }}
                >
                  设为默认
                </Button>
              )}
              <Button
                size="sm"
                variant="light"
                color="danger"
                onPress={async () => {
                  await RemoveJava(item.JavaPath);
                  await reload();
                }}
              >
                移除
              </Button>
            </li>
          ))}
        </ul>
      )}

      <div className="pt-4">
        <div className="text-sm text-gray-800 dark:text-gray-200 mb-2">
          已安装的托管运行时
        </div>
        {runtimes.length === 0 ? (
          <div className="text-xs text-gray-400">暂无</div>
        ) : (
          <ul className="space-y-1">
            {runtimes.map((rt, i) => (
              <li key={i} className="flex items-center gap-2 text-sm py-1">
                <span className="truncate flex-1 text-gray-700 dark:text-gray-300">
                  {rt.JavaExecutablePath || rt.DirectoryPath || '(未知)'}
                </span>
                {rt.MajorVersion ? (
                  <Chip size="sm" variant="flat">Java {rt.MajorVersion}</Chip>
                ) : null}
                {rt.Vendor !== undefined && rt.Vendor !== null ? (
                  <Chip size="sm" variant="flat">
                    {vendorLabel(rt.Vendor)}
                  </Chip>
                ) : null}
                <Button
                  size="sm"
                  variant="light"
                  color="danger"
                  onPress={async () => {
                    if (rt.DirectoryPath) {
                      await DeleteJavaRuntime(rt.DirectoryPath);
                      await reload();
                    }
                  }}
                >
                  删除
                </Button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Section>
  );
};

function vendorLabel(vendor: number): string {
  switch (vendor) {
    case 0: return 'Unknown';
    case 1: return 'Temurin';
    case 2: return 'Zulu';
    case 3: return 'Corretto';
    default: return `Vendor ${vendor}`;
  }
}

export default JavaSection;