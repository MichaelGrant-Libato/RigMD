import { reportDocumentText, type ReportDocument } from './reportFormatting.ts';
import { buildReportHtml } from './reportHtml.ts';

export async function copyReport(report: ReportDocument, clipboard: Clipboard = navigator.clipboard, Item = globalThis.ClipboardItem) {
  const text = reportDocumentText(report);
  if (clipboard?.write && Item) {
    try {
      await clipboard.write([new Item({
        'text/html': new Blob([buildReportHtml(report)], { type: 'text/html' }),
        'text/plain': new Blob([text], { type: 'text/plain' }),
      })]);
      return 'formatted' as const;
    } catch {
      // Some desktop hosts permit plain text but do not support rich clipboard content.
    }
  }
  await clipboard.writeText(text);
  return 'text' as const;
}
