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
import { Button, Select, SelectItem, Input } from '@heroui/react';
import Section, { SettingRow } from './Section';
import {
  GetAllDownloadSources, GetActiveDownloadSourceName, SaveActiveDownloadSource,
  GetFallbackDownloadSourceName, SaveFallbackDownloadSource,
  GetParallelDownloads, SaveParallelDownloads,
} from '../../../wailsjs/go/bindings/DownloadAPI';
import type { download } from '../../../wailsjs/go/models';

const DownloadSection: React.FC = () => {
  const [sources, setSources] = useState<download.DownloadSource[]>([]);
  const [active, setActive] = useState('');
  const [fallback, setFallback] = useState('');
  const [parallel, setParallel] = useState(4);

  useEffect(() => {
    (async () => {
      const [list, a, f, p] = await Promise.all([
        GetAllDownloadSources(), GetActiveDownloadSourceName(),
        GetFallbackDownloadSourceName(), GetParallelDownloads(),
      ]);
      setSources(list ?? []);
      setActive(a ?? '');
      setFallback(f ?? '');
      setParallel(p || 4);
    })();
  }, []);

  const changeActive = async (name: string) => {
    const src = sources.find(s => s.Name === name);
    if (src) {
      await SaveActiveDownloadSource(src);
      setActive(name);
    }
  };

  const changeFallback = async (name: string) => {
    if (name === '__none__') {
      await SaveFallbackDownloadSource(null as unknown as download.DownloadSource);
      setFallback('');
      return;
    }
    const src = sources.find(s => s.Name === name);
    if (src) {
      await SaveFallbackDownloadSource(src);
      setFallback(name);
    }
  };

  return (
    <Section title="下载" description="游戏文件的下载来源">
      <SettingRow label="下载源">
        <Select
          size="sm"
          className="w-56"
          selectedKeys={[active]}
          onSelectionChange={(keys) => changeActive(Array.from(keys)[0] as string)}
          items={sources}
        >
          {(item) => <SelectItem key={item.Name}>{item.Name}</SelectItem>}
        </Select>
      </SettingRow>

      <SettingRow label="自动回退源" hint="主下载源失败就会用">
        <Select
          size="sm"
          className="w-56"
          selectedKeys={[fallback || '__none__']}
          onSelectionChange={(keys) => changeFallback(Array.from(keys)[0] as string)}
          items={[
            { Name: '__none__', __label: '禁用' } as any,
            ...sources,
          ]}
        >
          {(item: any) => (
            <SelectItem key={item.Name}>
              {item.__label ?? item.Name}
            </SelectItem>
          )}
        </Select>
      </SettingRow>

      <SettingRow label="并行下载线程数">
        <Input
          size="sm"
          type="number"
          value={String(parallel)}
          onValueChange={(v) => setParallel(Number(v) || 4)}
          className="w-24"
        />
      </SettingRow>

      <div className="pt-1">
        <Button
          size="sm"
          color="primary"
          onPress={async () => { await SaveParallelDownloads(parallel); }}
        >
          保存
        </Button>
      </div>
    </Section>
  );
};

export default DownloadSection;