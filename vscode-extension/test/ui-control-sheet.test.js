'use strict';
const assert = require('node:assert/strict');
const test = require('node:test');
const { mountUiControlSheets, controlSheetExport } = require('../lib/ui-control-sheet');
const { renderUiBaseline } = require('../lib/ui-baseline');

const jpeg = `data:image/jpeg;base64,${Buffer.from([0xff, 0xd8, 0xff, 0xda, 0, 0xff, 0xd9]).toString('base64')}`;
function harness(failed = false) {
  const sent = [], drawn = [], status = { textContent: '' }, preview = { src: 'data:image/svg+xml;base64,svg' };
  const figure = { dataset: { controlSheet: '0', sourceHash: 'current' }, querySelector: selector => selector === 'img' ? preview : status };
  const canvas = { getContext: () => ({ fillRect() {}, drawImage: image => drawn.push(image) }), toDataURL: (mime, quality) => { assert.equal(mime, 'image/jpeg'); assert.equal(quality, 0.94); return jpeg; } };
  const document = { querySelectorAll: () => [figure], createElement: name => { assert.equal(name, 'canvas'); return canvas; } };
  class Image { naturalWidth = 1200; naturalHeight = 1000; set src(value) { assert.equal(value, preview.src); queueMicrotask(() => failed ? this.onerror() : this.onload()); } }
  return { sent, drawn, status, preview, canvas, sheet: mountUiControlSheets(document, Image, message => sent.push(message)) };
}

test('wizard renders a JPG automatically and export waits for rasterization without submitting answers', async () => {
  const h = harness();
  assert.equal(await h.sheet.save('0'), true);
  assert.equal(h.preview.src, jpeg); assert.equal(h.canvas.width, 1200); assert.equal(h.canvas.height, 1000);
  assert.equal(h.drawn.length, 1); assert.match(h.status.textContent, /JPG · 1200 × 1000/u);
  assert.equal(h.sent.length, 1); assert.equal(h.sent[0].command, 'save-ui-control-sheet');
  assert.deepEqual(JSON.parse(h.sent[0].value), { index: 0, sourceHash: 'current', data: jpeg });
});

test('failed rendering leaves the SVG visible and does not export or leave a pending operation', async () => {
  const h = harness(true);
  assert.equal(await h.sheet.save('0'), false);
  assert.match(h.preview.src, /^data:image\/svg/u); assert.match(h.status.textContent, /JPG rendering failed/u);
  assert.equal(h.sent.length, 0);
});

test('JPG export requires the current checked baseline and bounded valid image data', () => {
  const baseline = { sourceHash: 'current', repositories: [{ id: '../customer', preview: {} }] };
  const value = overrides => JSON.stringify({ index: 0, sourceHash: 'current', data: jpeg, ...overrides });
  const result = controlSheetExport(baseline, value());
  assert.equal(result.filename, '---customer-ui-controls.jpg'); assert.equal(result.bytes[0], 0xff);
  for (const request of [value({ index: -1 }), value({ index: 1 }), value({ sourceHash: 'stale' }), value({ data: 'data:text/html;base64,AAAA' }), value({ data: 'data:image/jpeg;base64,AAAA' }), '{'])
    assert.throws(() => controlSheetExport(baseline, request));
  assert.throws(() => controlSheetExport(baseline, ' '.repeat(6 * 1024 * 1024 + 1)));
});

test('baseline shows source-based control images ahead of details and preserves control evidence links', () => {
  const baseline = { sourceHash: 'current', repositories: [{ id: 'customer', repositoryPath: '/product/customer', filesRead: 1,
    facts: [], tokens: [], controls: [{ kind: 'buttons', title: 'Buttons', evidence: [{ relativePath: 'src/button.html', line: 6 }] }],
    preview: { svg: '<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="900"></svg>', width: 1200, height: 900, description: 'Source-based reference.' } }] };
  const html = renderUiBaseline(baseline, {});
  assert.match(html, /data-control-sheet="0" data-source-hash="current"/u);
  assert.match(html, /data:image\/svg\+xml;base64,/u); assert.match(html, /Save JPG control sheet/u);
  assert.match(html, /src\/button.html:6/u);
  assert.ok(html.indexOf('rendered reference controls') < html.indexOf('Framework, theme and typography'));
});
