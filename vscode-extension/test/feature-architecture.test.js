'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { architectureGallery } = require('../lib/feature-architecture');

test('feature C4 gallery shows rendered diagrams and recovery actions, not just product document links', () => {
  const result = { status: 'stale', summary: '<script>unsafe</script>', diagrams: [
    { id: 'context', title: 'C1 - Feature context', svg: '<svg/>', notes: 'Proposed boundary' },
    { id: 'containers', title: 'C2 - Containers', svg: '<svg/>', notes: 'Existing and proposed' },
    { id: 'components', title: 'C3 - Components', svg: '<svg/>', notes: 'Feature responsibilities' }] };
  const html = architectureGallery(result);
  assert.equal(html.match(/data:image\/svg\+xml;base64/g).length, 3);
  assert.match(html, /Save answers and regenerate C4 diagrams/);
  assert.match(html, /Open diagram in new tab/); assert.match(html, /Save SVG/);
  assert.match(html, /Regenerate to update/); assert.match(html, /&lt;script&gt;/); assert.doesNotMatch(html, /<script>/);
  const full = architectureGallery(result, { standalone: true });
  assert.match(full, /diagram-full/); assert.doesNotMatch(full, /data-wizard-action/);
  assert.match(architectureGallery(undefined), /No feature diagrams generated yet/);
});
