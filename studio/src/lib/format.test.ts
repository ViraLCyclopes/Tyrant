import { describe, expect, it } from 'vitest';
import { dumperLabel, formatBytes, outputLabel, summarizeDecompile, summarizeDump, summarizeRefresh } from './format';

describe('format', () => {
  it('names workspace outputs for people', () => {
    expect(outputLabel('data')).toBe('Game data');
    expect(outputLabel('assets/index')).toBe('Asset index');
    expect(outputLabel('source/Assembly-CSharp')).toBe('Code: Assembly-CSharp');
    expect(outputLabel('other')).toBe('other');
  });

  it('describes the dumper state', () => {
    expect(dumperLabel('installed')).toBe('Installed');
    expect(dumperLabel('notInstalled')).toBe('Not installed');
    expect(dumperLabel('conflict')).toMatch(/another mod loader/);
  });

  it('summarizes results in one line', () => {
    expect(summarizeDump({ objects: 7346, types: 132, languages: 11, errors: [] })).toBe('Dumped 7,346 objects of 132 types and 11 languages.');
    expect(summarizeDecompile({ assemblies: [{ assembly: 'A', success: true, error: null }, { assembly: 'B', success: false, error: 'B.dll not found.' }] }))
      .toBe('Decompiled 1 of 2 assemblies. B.dll not found.');
    expect(summarizeRefresh({ steps: [{ name: 'Decompile', status: 'ok', message: '6 done.' }, { name: 'Data dump', status: 'skipped', message: 'Not installed.' }] }))
      .toBe('Decompile: done · Data dump: skipped — Not installed.');
  });

  it('formats byte sizes', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(4096)).toBe('4.0 KB');
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB');
  });
});
