import type { ReportDocument, ReportField } from './reportFormatting.ts';

// Scope every rule to the report so the paper layout cannot alter the application.
export const reportStyles = `
.rigmd-report-paper { color-scheme: light; color: #182334; background: #fff; font: 14px/1.55 Arial, sans-serif; text-align: left; padding: 32px; overflow-wrap: anywhere; }
.rigmd-report-paper * { box-sizing: border-box; }
.rigmd-report-paper .report-header { border-bottom: 3px solid #167885; padding-bottom: 18px; margin-bottom: 22px; }
.rigmd-report-paper .report-brand { color: #167885; font-weight: 700; letter-spacing: .08em; font-size: 12px; margin: 0 0 6px; }
.rigmd-report-paper h1 { font: 700 27px/1.2 Arial, sans-serif; margin: 0 0 8px; color: #14283b; }
.rigmd-report-paper .report-subtitle { margin: 0; color: #475569; font-size: 16px; }
.rigmd-report-paper section { margin: 0 0 24px; }
.rigmd-report-paper h2 { font: 700 17px/1.35 Arial, sans-serif; margin: 0 0 10px; padding-bottom: 7px; border-bottom: 1px solid #cbd5e1; color: #173e4a; break-after: avoid; page-break-after: avoid; }
.rigmd-report-paper h3 { font: 700 14px/1.4 Arial, sans-serif; margin: 0 0 8px; color: #182334; break-after: avoid; page-break-after: avoid; }
.rigmd-report-paper p { margin: 6px 0 12px; }
.rigmd-report-paper table { width: 100%; border-collapse: collapse; table-layout: fixed; margin: 0 0 10px; font: inherit; }
.rigmd-report-paper th, .rigmd-report-paper td { vertical-align: top; padding: 6px 9px; border-bottom: 1px solid #e2e8f0; text-align: left; overflow-wrap: anywhere; white-space: pre-wrap; }
.rigmd-report-paper th { width: 31%; font-weight: 600; color: #475569; }
.rigmd-report-paper tr { break-inside: avoid; page-break-inside: avoid; }
.rigmd-report-paper .report-entry { margin: 12px 0; padding: 12px; border: 1px solid #d8e0e8; border-left: 3px solid #94a3b8; border-radius: 4px; break-inside: avoid; page-break-inside: avoid; }
.rigmd-report-paper .report-entry[data-status="attention"] { border-left-color: #9a6400; }
.rigmd-report-paper .report-entry[data-status="unknown"] { border-left-color: #64748b; }
.rigmd-report-paper .report-description, .rigmd-report-paper .report-note { color: #475569; font-size: 13px; }
.rigmd-report-paper .report-note { padding-left: 10px; border-left: 2px solid #cbd5e1; }
.rigmd-report-paper footer { border-top: 1px solid #cbd5e1; padding-top: 12px; color: #475569; font-size: 12px; break-inside: avoid; page-break-inside: avoid; }
.rigmd-report-paper .report-section-intro { break-inside: avoid; page-break-inside: avoid; }
.rigmd-report-paper .report-with-entries { break-after: avoid; page-break-after: avoid; }
@media screen and (max-width: 600px) {
  .rigmd-report-paper { padding: 18px; font-size: 13px; }
  .rigmd-report-paper h1 { font-size: 22px; }
  .rigmd-report-paper th { width: 38%; }
  .rigmd-report-paper th, .rigmd-report-paper td { padding: 6px 4px; }
  .rigmd-report-paper .report-entry { padding: 9px; }
}
@media print {
  @page { size: auto; margin: 15mm; }
  .rigmd-report-paper { padding: 0; font-size: 10.5pt; line-height: 1.45; }
  .rigmd-report-paper th, .rigmd-report-paper td { padding: 4px 7px; }
  .rigmd-report-paper section { margin-bottom: 18px; }
  .rigmd-report-paper .report-header { padding-bottom: 12px; margin-bottom: 16px; }
  .rigmd-report-paper .report-entry { padding: 9px; margin: 9px 0; }
  .rigmd-report-paper h1 { font-size: 23pt; }
  .rigmd-report-paper h2 { font-size: 13pt; }
  .rigmd-report-paper h3 { font-size: 11pt; }
  .rigmd-report-paper .report-description, .rigmd-report-paper .report-note { font-size: 9.5pt; }
  .rigmd-report-paper footer { font-size: 9pt; }
  .rigmd-report-paper p { orphans: 3; widows: 3; }
}
`;

// All dynamic text goes through this encoder, including names, readings and saved explanations.
export function escapeReportHtml(value: string) {
  return value.replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]!);
}
export function reportBodyHtml(report: ReportDocument) {
  const escape = escapeReportHtml;
  const fields = (rows: ReportField[] = []) => rows.length
    ? '<table><tbody>' + rows.map(row => `<tr><th scope="row">${escape(row.label)}</th><td>${escape(row.value)}</td></tr>`).join('') + '</tbody></table>' : '';
  const notes = (items: string[] = []) => items.map(item => `<p class="report-note">${escape(item)}</p>`).join('');
  return `<article class="rigmd-report-paper" aria-label="Device check report">
<header class="report-header"><p class="report-brand">RIGMD / DEVICE CHECK</p><h1>${escape(report.title)}</h1><p class="report-subtitle">${escape(report.subtitle)}</p></header>
${report.sections.map(section => `<section><div class="report-section-intro${section.entries?.length ? ' report-with-entries' : ''}"><h2>${escape(section.title)}</h2>${section.description ? `<p class="report-description">${escape(section.description)}</p>` : ''}${fields(section.fields)}${notes(section.notes)}</div>
${(section.entries ?? []).map(entry => `<div class="report-entry" data-status="${escape(entry.status ?? 'information')}"><h3>${escape(entry.title)}</h3>${fields(entry.fields)}${notes(entry.notes)}</div>`).join('')}</section>`).join('')}
<footer><strong>About this report</strong><p>${escape(report.footer)}</p></footer></article>`;
}
export function buildReportHtml(report: ReportDocument) {
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>${escapeReportHtml(report.title)}</title><style>${reportStyles}</style></head><body style="margin:0;background:#fff">${reportBodyHtml(report)}</body></html>`;
}
