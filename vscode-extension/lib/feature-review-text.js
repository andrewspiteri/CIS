'use strict';

const { escapeHtml: h } = require('./security');

// A presentation-only Markdown subset. Source HTML, images and URLs never become
// executable markup or remote requests; document navigation uses explicit CIS actions.
function inline(text) {
  return h(String(text).replace(/!?\[([^\]]*)\]\([^)]*\)/gu, '$1'))
    .replace(/`([^`]+)`/gu, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/gu, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/gu, '<em>$1</em>');
}

function renderReviewText(text) {
  const lines = String(text || '').replace(/<!--[^]*?-->/gu, '').replace(/\r\n/gu, '\n').split('\n');
  const result = [];
  const cells = line => line.trim().replace(/^\||\|$/gu, '').split(/(?<!\\)\|/u).map(cell => cell.trim().replace(/\\\|/gu, '|'));
  const separator = line => /^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$/u.test(line || '');
  for (let i = 0; i < lines.length;) {
    const line = lines[i].trim();
    if (!line) { i++; continue; }
    if (/^```/u.test(line)) {
      const body = []; i++;
      while (i < lines.length && !/^```/u.test(lines[i].trim())) body.push(lines[i++]);
      if (i < lines.length) i++;
      result.push(`<pre><code>${h(body.join('\n'))}</code></pre>`); continue;
    }
    if (line.includes('|') && separator(lines[i + 1])) {
      const headers = cells(line); const rows = []; i += 2;
      while (i < lines.length && lines[i].trim() && lines[i].includes('|')) {
        const row = cells(lines[i++]);
        rows.push(`<tr>${headers.map((_, column) => `<td>${inline(row[column] || '')}</td>`).join('')}</tr>`);
      }
      result.push(`<div class="feature-table"><table><thead><tr>${headers.map(cell => `<th scope="col">${inline(cell)}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>`); continue;
    }
    const heading = /^(#{1,6})\s+(.+)$/u.exec(line);
    if (heading) { result.push(`<h4>${inline(heading[2])}</h4>`); i++; continue; }
    if (/^(?:[-*_]\s*){3,}$/u.test(line)) { result.push('<hr>'); i++; continue; }
    const list = /^(?:[-*+]\s+|\d+[.)]\s+)/u.exec(line);
    if (list) {
      const tag = /^\d/u.test(list[0]) ? 'ol' : 'ul'; const items = [];
      const pattern = tag === 'ol' ? /^\d+[.)]\s+/u : /^[-*+]\s+/u;
      while (i < lines.length && pattern.test(lines[i].trim())) items.push(`<li>${inline(lines[i++].trim().replace(pattern, ''))}</li>`);
      result.push(`<${tag}>${items.join('')}</${tag}>`); continue;
    }
    const paragraph = [line]; i++;
    while (i < lines.length && lines[i].trim() && !/^(?:#{1,6}\s|```|[-*+]\s|\d+[.)]\s)/u.test(lines[i].trim()) && !separator(lines[i + 1])) paragraph.push(lines[i++].trim());
    result.push(`<p>${paragraph.map(inline).join('<br>')}</p>`);
  }
  return result.join('');
}

const REVIEW_STYLES = `
.feature-hero{padding-bottom:.5rem}.feature-hero p{margin:.25rem 0}.feature-hero h1{margin:.2rem 0}.feature-saved{margin-top:.75rem}
.feature-review-progress{display:flex;gap:.6rem;align-items:center;flex-wrap:wrap;margin:.6rem 0}
.feature-question-index{display:grid;gap:.5rem}.feature-question-index button{text-align:left}
.feature-review-text{line-height:1.6;overflow-wrap:anywhere;min-width:0}.feature-review-text p{max-width:none}
.feature-review-text h4{margin:1rem 0 .4rem}.feature-review-text ul,.feature-review-text ol{padding-left:1.5rem}
.feature-review-text pre{white-space:pre-wrap;overflow-wrap:anywhere}.feature-review-text table{width:100%;border-collapse:collapse;font-size:.93em}
.feature-review-text th,.feature-review-text td{padding:.6rem;border-bottom:1px solid var(--vscode-panel-border);text-align:left;vertical-align:top}
.feature-review-text th{width:auto;background:var(--vscode-editor-background)}.feature-table{overflow-x:auto}
.feature-answer-editor{margin-top:1rem}.feature-answer-editor summary{cursor:pointer;font-weight:600}.feature-answer-editor textarea{margin-top:.65rem;min-height:8rem}
.feature-source-context{margin:1rem 0;border-top:1px solid var(--vscode-panel-border);padding-top:.6rem}.feature-source-context summary{cursor:pointer;font-weight:600}
.feature-review-actions{position:sticky;bottom:0;z-index:2;background:var(--vscode-editor-background);border-top:1px solid var(--vscode-panel-border);padding:.8rem 0;display:flex;gap:.6rem;flex-wrap:wrap;align-items:center}
.feature-review-actions .muted{flex-basis:100%}.feature-question{scroll-margin-top:1rem}.feature-question h3{margin:.4rem 0;font-size:1.05rem}
.feature-review-form fieldset{padding:0;border:0;min-width:0}.feature-review-form legend{margin-bottom:1rem}
@media(max-width:700px){.feature-review-actions button{flex:1 1 12rem}.wizard-layout{grid-template-columns:1fr}.wizard-steps{position:static!important}}
`;

module.exports = { renderReviewText, REVIEW_STYLES };
