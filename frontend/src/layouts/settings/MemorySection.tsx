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
import { Slider, Switch } from '@heroui/react';
import Section, { SettingRow } from './Section';
import {
  GetMemorySliderMaximum, GetManualMaximumMemoryMb, SaveManualMaximumMemoryMb,
  IsAutomaticMemoryAdjustmentEnabled, SetAutomaticMemoryAdjustmentEnabled,
  GetSystemMemory,
} from '../../../wailsjs/go/bindings/LauncherAPI';

const MemorySection: React.FC = () => {
  const [systemTotalMb, setSystemTotalMb] = useState(0);
  const [sliderMax, setSliderMax] = useState(8192);
  const [memoryMb, setMemoryMb] = useState(4096);
  const [isAuto, setIsAuto] = useState(true);
  const [, setIsLoading] = useState(true);
  const [saveHint, setSaveHint] = useState('');

  useEffect(() => {
    (async () => {
      const [sys, max, cur, auto] = await Promise.all([
        GetSystemMemory(), GetMemorySliderMaximum(),
        GetManualMaximumMemoryMb(), IsAutomaticMemoryAdjustmentEnabled(),
      ]);
      setSystemTotalMb(sys?.TotalMemoryMb ?? 0);
      setSliderMax(max || 8192);
      const clamped = Math.min(Math.max(cur || 4096, 512), max || 8192);
      setMemoryMb(clamped);
      setIsAuto(auto);
      setIsLoading(false);
    })();
  }, []);

  const commit = async (mb: number) => {
    const ok = await SaveManualMaximumMemoryMb(mb);
    setSaveHint(ok ? `已保存：${mb} MB` : '保存失败');
    setTimeout(() => setSaveHint(''), 2000);
  };

  return (
    <Section title="内存" description="全局默认的内存上限，每个实例都能单独覆盖">
      <SettingRow label="自动调整内存" hint="根据可用内存自动决定，每次启动时自动调整">
        <Switch isSelected={isAuto} onValueChange={async (v) => { setIsAuto(v); await SetAutomaticMemoryAdjustmentEnabled(v); }} color="primary" />
      </SettingRow>

      <div className={`py-3 ${isAuto ? 'opacity-50 pointer-events-none' : ''}`}>
        <div className="flex items-center justify-between mb-2">
          <div className="text-sm text-gray-800 dark:text-gray-200">最大内存</div>
          <div className="text-sm text-primary font-medium tabular-nums">{memoryMb}MB</div>
        </div>
        <Slider
          aria-label="最大内存"
          minValue={512}
          maxValue={sliderMax}
          step={256}
          value={memoryMb}
          fillOffset={512}
          onChange={(v) => setMemoryMb(Array.isArray(v) ? v[0] : v)}
          onChangeEnd={(v) => commit(Array.isArray(v) ? v[0] : v)}
          color="primary"
          showTooltip
          getValue={(v) => `${v} MB`}
        />
        <div className="flex items-center justify-between mt-1 text-xs text-gray-400">
          <span>512 MB</span><span>{sliderMax}MB</span>
        </div>
        {systemTotalMb > 0 && (
          <div className="text-xs text-gray-400 mt-2">系统总内存{systemTotalMb}MB</div>
        )}
        {saveHint && <div className="text-xs text-primary mt-2">{saveHint}</div>}
      </div>
    </Section>
  );
};

export default MemorySection;