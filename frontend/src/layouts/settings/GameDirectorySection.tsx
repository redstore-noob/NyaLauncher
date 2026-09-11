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
import { Button, Input } from '@heroui/react';
import Section, { SettingRow } from './Section';
import {
  GetGameDirectory, SaveGameDirectory, ClearGameDirectory,
  GetProfileFolders, AddProfileFolder, RemoveProfileFolder,
} from '../../../wailsjs/go/bindings/ConfigAPI';
import { SelectDirectory } from '../../../wailsjs/go/bindings/SystemAPI';

const GameDirectorySection: React.FC = () => {
  const [gameDir, setGameDir] = useState('');
  const [folders, setFolders] = useState<string[]>([]);

  const reload = async () => {
    const [dir, list] = await Promise.all([GetGameDirectory(), GetProfileFolders()]);
    setGameDir(dir ?? '');
    setFolders(list ?? []);
  };

  useEffect(() => { reload(); }, []);

  const pickDir = async () => {
    const picked = await SelectDirectory('选择Minecraft目录');
    if (picked) {
      await SaveGameDirectory(picked);
      await reload();
    }
  };

  const addFolder = async () => {
    const picked = await SelectDirectory('选择额外扫描的目录');
    if (picked) {
      await AddProfileFolder(picked);
      await reload();
    }
  };

  const removeFolder = async (path: string) => {
    await RemoveProfileFolder(path);
    await reload();
  };

  return (
    <Section title="游戏目录" description="主目录用于扫描已安装版本，额外目录会被一并扫描">
      <SettingRow label="主目录">
        <div className="flex items-center gap-2">
          <Input
            size="sm"
            value={gameDir}
            readOnly
            placeholder="未设置"
            className="w-64"
          />
          <Button size="sm" variant="flat" onPress={pickDir}>选择</Button>
          <Button size="sm" variant="light" onPress={async () => { await ClearGameDirectory(); await reload(); }}>
            清除
          </Button>
        </div>
      </SettingRow>

      <div className="pt-2">
        <div className="flex items-center justify-between mb-2">
          <div className="text-sm text-gray-800 dark:text-gray-200">额外扫描的目录</div>
          <Button size="sm" variant="flat" onPress={addFolder}>添加</Button>
        </div>
        {folders.length === 0 ? (
          <div className="text-xs text-gray-400">暂无</div>
        ) : (
          <ul className="space-y-1">
            {folders.map((f) => (
              <li key={f} className="flex items-center justify-between gap-2 text-sm">
                <span className="truncate text-gray-700 dark:text-gray-300">{f}</span>
                <Button size="sm" variant="light" color="danger" onPress={() => removeFolder(f)}>
                  移除
                </Button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Section>
  );
};

export default GameDirectorySection;