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

interface SectionProps {
  title: string;
  description?: string;
  children: React.ReactNode;
}

const Section: React.FC<SectionProps> = ({ title, description, children }) => {
  return (
    <section>
      <div className="mb-3">
        <h2 className="text-md font-semibold text-black dark:text-gray-400">
          {title}
        </h2>
        {description && (
          <p className="text-xs text-gray-400 mt-0.5">{description}</p>
        )}
      </div>
      <div className="space-y-3">{children}</div>
    </section>
  );
};

export default Section;

export const SettingRow: React.FC<{
  label: string;
  hint?: string;
  children: React.ReactNode;
}> = ({ label, hint, children }) => (
  <div className="flex items-center justify-between py-2 gap-4">
    <div className="min-w-0">
      <div className="text-sm text-gray-800 dark:text-gray-200">{label}</div>
      {hint && <div className="text-xs text-gray-400 mt-0.5">{hint}</div>}
    </div>
    <div className="flex-shrink-0">{children}</div>
  </div>
);