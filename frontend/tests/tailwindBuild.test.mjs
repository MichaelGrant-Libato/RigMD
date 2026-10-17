import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import postcss from 'postcss';
import config from '../postcss.config.js';

test('configured PostCSS pipeline compiles Tailwind utilities and preserves RigMD styles', async () => {
  const plugins = [];
  for (const [name, options] of Object.entries(config.plugins)) {
    const { default: plugin } = await import(name);
    plugins.push(plugin(options));
  }

  const sourcePath = fileURLToPath(new URL('../src/index.css', import.meta.url));
  const source = await readFile(sourcePath, 'utf8');
  const result = await postcss(plugins).process(source, { from: sourcePath });
  const selectors = new Set();
  result.root.walkRules((rule) => {
    for (const selector of rule.selectors) selectors.add(selector);
  });

  assert.ok(selectors.has('.flex'), 'Tailwind layout utilities must be generated');
  assert.ok(selectors.has('.rigmd-glass'), 'RigMD custom styles must be retained');
  assert.match(result.css, /Inter/, 'The existing font configuration must be loaded');
  result.root.walkAtRules((rule) => {
    assert.notEqual(rule.name, 'config', 'Tailwind must consume the legacy configuration');
    assert.ok(rule.name !== 'import' || !rule.params.includes('tailwindcss'));
    assert.ok(rule.name !== 'tailwind' || !/^(base|components|utilities)$/.test(rule.params));
  });
});
