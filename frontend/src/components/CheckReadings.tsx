import DiagnosticReadings from './DiagnosticReadings';
import { crashMetric, type CheckPresentationInput } from '../lib/checkPresentation';

export default function CheckReadings({ check }: { check: CheckPresentationInput }) {
  const metric = crashMetric(check);
  if (!metric) return <DiagnosticReadings readings={check.proof} />;
  return <div className="space-y-5">
    <DiagnosticReadings readings={check.proof?.filter(row => row.metric_id === metric)} title="Crash check results" />
    <DiagnosticReadings readings={check.proof?.filter(row => row.metric_id !== metric)} title="Other readings and findings" />
  </div>;
}
