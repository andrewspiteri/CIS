'use strict';

// Runs inside the webview; no product code, fonts, URLs or package scripts are loaded.
function mountUiControlSheets(document, Image, postMessage) {
  const sheets = new Map();
  const ready = Promise.all(Array.from(document.querySelectorAll('[data-control-sheet]')).map(async figure => {
    const preview = figure.querySelector('img');
    const status = figure.querySelector('[data-render-status]');
    const index = figure.dataset.controlSheet;
    try {
      const image = new Image();
      await new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('Image load timed out')), 5000);
        image.onload = () => { clearTimeout(timer); resolve(); };
        image.onerror = () => { clearTimeout(timer); reject(new Error('Image load failed')); };
        image.src = preview.src;
      });
      if (!image.naturalWidth || !image.naturalHeight || image.naturalWidth > 4096 || image.naturalHeight > 4096) throw new Error('Image bounds');
      const canvas = document.createElement('canvas');
      canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
      const context = canvas.getContext('2d');
      context.fillStyle = '#ffffff'; context.fillRect(0, 0, canvas.width, canvas.height);
      context.drawImage(image, 0, 0);
      const jpeg = canvas.toDataURL('image/jpeg', 0.94);
      if (!jpeg.startsWith('data:image/jpeg;base64,')) throw new Error('JPEG encoder unavailable');
      sheets.set(index, jpeg); preview.src = jpeg;
      status.textContent = `JPG · ${canvas.width} × ${canvas.height} · Source-based control reference`;
    } catch {
      status.textContent = 'JPG rendering failed. The SVG reference remains visible; reopen the page to retry.';
    }
  }));
  return {
    ready,
    async save(index) {
      await ready;
      const data = sheets.get(index);
      const figure = Array.from(document.querySelectorAll('[data-control-sheet]')).find(item => item.dataset.controlSheet === index);
      if (!data || !figure) return false;
      postMessage({ command: 'save-ui-control-sheet', value: JSON.stringify({ index: Number(index), sourceHash: figure.dataset.sourceHash, data }) });
      return true;
    },
  };
}

function controlSheetExport(baseline, value) {
  if (typeof value !== 'string' || value.length > 6 * 1024 * 1024) throw new Error('The control sheet is too large.');
  let request;
  try { request = JSON.parse(value); } catch { throw new Error('The control sheet export is invalid.'); }
  const repository = Number.isSafeInteger(request?.index) && request.index >= 0 && baseline?.repositories?.[request.index];
  if (!baseline?.sourceHash || request?.sourceHash !== baseline.sourceHash || !repository?.preview)
    throw new Error('The UI baseline changed. Refresh this page before exporting.');
  if (typeof request.data !== 'string' || !/^data:image\/jpeg;base64,[A-Za-z0-9+/]+={0,2}$/u.test(request.data))
    throw new Error('Only a rendered JPG control sheet can be exported.');
  const bytes = Buffer.from(request.data.slice('data:image/jpeg;base64,'.length), 'base64');
  if (bytes.length < 4 || bytes[0] !== 0xff || bytes[1] !== 0xd8 || bytes[2] !== 0xff || bytes.at(-2) !== 0xff || bytes.at(-1) !== 0xd9)
    throw new Error('The rendered JPG is incomplete. Reopen the page to retry.');
  return { bytes, filename: `${String(repository.id).replace(/[^a-zA-Z0-9_-]/gu, '-').slice(0, 100)}-ui-controls.jpg` };
}

module.exports = { mountUiControlSheets, controlSheetExport };
