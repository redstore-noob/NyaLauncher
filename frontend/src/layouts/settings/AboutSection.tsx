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
import { Button } from '@heroui/react';
import Section, { SettingRow } from './Section';
import { GetFormattedVersion, ClearLogs } from '../../../wailsjs/go/bindings/SystemAPI';
import { GetStorageDirectory } from '../../../wailsjs/go/bindings/ConfigAPI';
import { OpenPath } from '../../../wailsjs/go/bindings/SystemAPI';

const AboutSection: React.FC = () => {
  const [version, setVersion] = useState('');
  const [storage, setStorage] = useState('');
  const [cleared, setCleared] = useState<number | null>(null);

  useEffect(() => {
    (async () => {
      setVersion(await GetFormattedVersion());
      setStorage(await GetStorageDirectory());
    })();
  }, []);

  return (
    <Section title="关于与维护">
      <SettingRow label="版本">
        <span className="text-xs text-gray-600 dark:text-gray-300 tabular-nums">{version}</span>
      </SettingRow>

      <SettingRow label="存储目录">
        <div className="flex items-center gap-2">
          <Button size="sm" variant="light" onPress={() => OpenPath(storage)}>打开</Button>
          <span className="text-xs text-gray-600 truncate max-w-[18rem]">{storage}</span>
        </div>
      </SettingRow>

      <SettingRow label="日志" hint={cleared != null ? `已清除${cleared}个文件` : undefined}>
        <Button size="sm" variant="flat" color="danger" onPress={async () => {
          const n = await ClearLogs();
          setCleared(n);
          setTimeout(() => setCleared(null), 3000);
        }}>
          清空
        </Button>
      </SettingRow>
    </Section>
  );
};

export default AboutSection;