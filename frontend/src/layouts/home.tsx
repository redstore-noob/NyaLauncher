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
import React, { useState, useEffect } from 'react';
import { Button, Listbox, ListboxItem, ScrollShadow, Spinner } from '@heroui/react';
import { CubeIcon, PlayIcon, RefreshIcon } from '../icons';
import {
  GetInstalledVersionIds,
  EnsureDefaultMinecraftDirectory,
} from '../../wailsjs/go/bindings/InstanceAPI';
import { GetGameDirectory } from '../../wailsjs/go/bindings/ConfigAPI';

//背景图
const BING_BACKGROUND_URL = 'https://api.ffis.me/bing/bing-images.php';

const HomePage: React.FC = () => {
  const [versions, setVersions] = useState<string[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string>('');
  const [minecraftDirectory, setMinecraftDirectory] = useState<string>('');
  const [selectedVersion, setSelectedVersion] = useState<string>('');
  const [isLaunching, setIsLaunching] = useState(false);

  const loadVersions = async () => {
    setIsLoading(true);
    setLoadError('');
    try {
      let directory = await GetGameDirectory();
      if (!directory) {
        directory = await EnsureDefaultMinecraftDirectory();
      }
      if (!directory) {
        setVersions([]);
        setLoadError('无法确定Minecraft目录');
        return;
      }
      setMinecraftDirectory(directory);
      const list = await GetInstalledVersionIds(directory);
      const versionList = list ?? [];
      setVersions(versionList);
      setSelectedVersion((prev) =>
        prev && versionList.includes(prev) ? prev : versionList[0] ?? ''
      );
    } catch (err) {
      console.error(err);
      setVersions([]);
      setLoadError(err instanceof Error ? err.message : String(err));
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    loadVersions();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleLaunch = () => {
    if (!selectedVersion) return;
    setIsLaunching(true);
    setTimeout(() => setIsLaunching(false), 1500);
  };

  return (
    <div
      className="relative h-full w-full bg-cover bg-center bg-no-repeat"
      style={{ backgroundImage: `url(${BING_BACKGROUND_URL})` }}
    >
      {/**/}
      <div className="absolute inset-0 bg-black/10 dark:bg-black/30" />

      {/* 前景 */}
      <div className="relative flex h-full w-full">
        {/*左侧空白区*/}
        <div className="flex-1 backdrop-blur-sm bg-transparent" />

        {/*右侧启动栏*/}
        <aside
          className="
            w-[35%] min-w-[360px] max-w-[480px]
            flex flex-col h-full flex-shrink-0
            bg-white/80 dark:bg-gray-900/70
            backdrop-blur-sm
          "
        >
          {/*未实现*/}
          <div className="p-4 flex-shrink-0">
            <Button
              variant="flat"
              color="primary"
              fullWidth
              onPress={() => {}}
              className="justify-start font-medium rounded-xl"
            >
              点击设置账户 &gt;
            </Button>
          </div>

          {/*版本列表*/}
          <div className="flex-1 overflow-hidden flex flex-col">
            <div className="px-4 pt-3 pb-1 flex items-center justify-between flex-shrink-0">
              <h2 className="text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider">
                版本列表
              </h2>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={loadVersions}
                isDisabled={isLoading}
                title="刷新版本列表"
                className="text-gray-500 min-w-7 w-7 h-7"
              >
                <RefreshIcon />
              </Button>
            </div>

            <ScrollShadow className="flex-1 px-2">
              {isLoading ? (
                <div className="flex flex-col items-center justify-center py-10 gap-2 text-gray-500">
                  <Spinner size="sm" />
                  <span className="text-xs">正在读取已安装版本…</span>
                </div>
              ) : loadError ? (
                <div className="px-3 py-6 text-sm text-red-500 text-center">{loadError}</div>
              ) : versions.length === 0 ? (
                <div className="px-3 py-6 text-sm text-gray-500 text-center">
                  未找到已安装的版本
                  {minecraftDirectory && (
                    <div className="mt-2 text-[11px] text-gray-400 break-all">
                      {minecraftDirectory}
                    </div>
                  )}
                </div>
              ) : (
                <Listbox
                  aria-label="版本列表"
                  selectionMode="single"
                  selectedKeys={selectedVersion ? [selectedVersion] : []}
                  onSelectionChange={(keys) => {
                    const key = Array.from(keys)[0] as string;
                    if (key) setSelectedVersion(key);
                  }}
                  classNames={{ base: 'p-0', list: 'gap-0.5' }}
                  itemClasses={{
                    base: 'data-[hover=true]:bg-blue-50/70 dark:data-[hover=true]:bg-blue-900/30 rounded-lg',
                    title: 'text-sm font-medium',
                  }}
                >
                  {versions.map((versionId) => (
                    <ListboxItem
                      key={versionId}
                      textValue={versionId}
                      startContent={
                        <span className="text-gray-500">
                          <CubeIcon />
                        </span>
                      }
                    >
                      <span className="truncate">{versionId}</span>
                    </ListboxItem>
                  ))}
                </Listbox>
              )}
            </ScrollShadow>
          </div>

          {/*底部启动按钮*/}
          <div className="p-4 flex-shrink-0">
            <Button
              color="primary"
              size="lg"
              fullWidth
              isLoading={isLaunching}
              isDisabled={!selectedVersion}
              onPress={handleLaunch}
              startContent={!isLaunching ? <PlayIcon /> : undefined}
              className="font-semibold rounded-xl"
            >
              {isLaunching ? '启动中..' : '启动游戏'}
            </Button>
          </div>
        </aside>
      </div>
    </div>
  );
};

export default HomePage;