import { reportBodyHtml, reportStyles } from '../lib/reportHtml';
import type { ReportDocument as Report } from '../lib/reportFormatting';

export default function ReportDocument({ report }: { report: Report }) {
  // The serializer escapes every field; it also generates the standalone download.
  return <><style>{reportStyles}</style><div dangerouslySetInnerHTML={{ __html: reportBodyHtml(report) }} /></>;
}
