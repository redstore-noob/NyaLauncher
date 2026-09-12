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
import { Button, Input, Switch, Textarea } from '@heroui/react';
import Section, { SettingRow } from './Section';
import {
  LoadGlobalLaunchSettings, SaveGlobalLaunchSettings,
  GetDefaultVersionIsolation, SaveDefaultVersionIsolation,
  GetVerifyFilesBeforeLaunch, SaveVerifyFilesBeforeLaunch,
} from '../../../wailsjs/go/bindings/ConfigAPI';

interface LaunchSettings {
  WindowWidth: number;
  WindowHeight: number;
  JavaExecutable: string;
  AdditionalJvmArguments: string[];
  AdditionalGameArguments: string[];
}

const LaunchSection: React.FC = () => {
  const [s, setS] = useState<LaunchSettings>({
    WindowWidth: 854, WindowHeight: 480, JavaExecutable: '',
    AdditionalJvmArguments: [], AdditionalGameArguments: [],
  });
  const [jvmArgs, setJvmArgs] = useState('');
  const [gameArgs, setGameArgs] = useState('');
  const [isolation, setIsolation] = useState<boolean>(true);
  const [verify, setVerify] = useState<boolean>(true);

  useEffect(() => {
    (async () => {
      const [loaded, iso, ver] = await Promise.all([
        LoadGlobalLaunchSettings(),
        GetDefaultVersionIsolation(),
        GetVerifyFilesBeforeLaunch(),
      ]);
      setS(loaded);
      setJvmArgs((loaded.AdditionalJvmArguments ?? []).join('\n'));
      setGameArgs((loaded.AdditionalGameArguments ?? []).join('\n'));
      setIsolation(iso);
      setVerify(ver);
    })();
  }, []);

  const save = async () => {
    const settings = {
      ...s,
      AdditionalJvmArguments: jvmArgs.split('\n').map(x => x.trim()).filter(Boolean),
      AdditionalGameArguments: gameArgs.split('\n').map(x => x.trim()).filter(Boolean),
    };
    await SaveGlobalLaunchSettings(settings);
    setS(settings);
  };

  return (
    <Section title="游戏启动" description="对所有实例生效，实例单独覆盖时以实例设置优先">
      <SettingRow label="启动前校验文件" hint="缺失文件会被自动补全">
        <Switch isSelected={verify} onValueChange={async (v) => { setVerify(v); await SaveVerifyFilesBeforeLaunch(v); }} color="primary" />
      </SettingRow>

      <SettingRow label="默认版本隔离" hint="新实例是否把各自的游戏数据文件（如模组等）分到各自目录">
        <Switch isSelected={isolation} onValueChange={async (v) => { setIsolation(v); await SaveDefaultVersionIsolation(v); }} color="primary" />
      </SettingRow>

      <SettingRow label="窗口宽度">
        <Input size="sm" type="number" value={String(s.WindowWidth)} onChange={e => setS({ ...s, WindowWidth: Number(e.target.value) })} className="w-28" />
      </SettingRow>

      <SettingRow label="窗口高度">
        <Input size="sm" type="number" value={String(s.WindowHeight)} onChange={e => setS({ ...s, WindowHeight: Number(e.target.value) })} className="w-28" />
      </SettingRow>

      <div className="pt-2">
        <div className="text-sm text-gray-800 dark:text-gray-200 mb-1">附加jvm参数</div>
        <div className="text-xs text-gray-400 mb-2">如-XX:+UseG1GC，每行一个</div>
        <Textarea minRows={3} value={jvmArgs} onValueChange={setJvmArgs} />
      </div>

      <div className="pt-2">
        <div className="text-sm text-gray-800 dark:text-gray-200 mb-1">附加游戏参数</div>
        <div className="text-xs text-gray-400 mb-2">如--fullscreen，每行一个</div>
        <Textarea minRows={3} value={gameArgs} onValueChange={setGameArgs} />
      </div>

      <div className="pt-2">
        <Button color="primary" size="sm" onPress={save}>保存</Button>
      </div>
    </Section>
  );
};

export default LaunchSection;