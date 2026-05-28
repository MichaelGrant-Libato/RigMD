import {
  AlertTriangle,
  BookOpen,
  CheckCircle2,
  ChevronDown,
  Info,
  Stethoscope,
  XCircle,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';

const supportedItems = [
  {
    title: 'Slow Boot',
    description: 'Identify probable causes of extended startup times and recommend maintenance or troubleshooting steps.',
  },
  {
    title: 'System Freezing',
    description: 'Connect freeze patterns with RAM pressure, thermal behavior, storage state, or driver-related causes.',
  },
  {
    title: 'Blue Screen Symptoms',
    description: 'Map BSOD-style symptoms and driver clues to probable internal causes and action categories.',
  },
  {
    title: 'Driver Conflicts',
    description: 'Use reported behavior and Device Manager-style warning signs to guide update or rollback decisions.',
  },
  {
    title: 'Storage Warning Indicators',
    description: 'Interpret disk usage, slow file access, and storage-related warnings for maintenance planning.',
  },
  {
    title: 'Application Crashes',
    description: 'Correlate repeated app crashes with software conflicts, memory pressure, or driver problems.',
  },
  {
    title: 'Display-Related Behavior',
    description: 'Triage flickering, black-screen, rendering, and no-display symptoms against GPU or driver causes.',
  },
  {
    title: 'Basic No-Power Triage',
    description: 'Guide users through observable no-power symptoms before recommending physical inspection.',
  },
];

const unsupportedItems = [
  {
    title: 'Physically Inspect Hardware',
    description: 'RigMD cannot see, touch, test, reseat, or visually inspect physical PC components.',
  },
  {
    title: 'Measure Voltage or Power',
    description: 'Electrical measurements require physical tools. RigMD cannot assess PSU output or component power draw.',
  },
  {
    title: 'Replace or Repair Parts',
    description: 'RigMD provides advisory support only. It does not perform repairs or hardware replacements.',
  },
  {
    title: 'Recommend Purchases',
    description: 'RigMD is a diagnostic support tool, not a buying guide or upgrade advisor.',
  },
  {
    title: 'Guarantee a Confirmed Diagnosis',
    description: 'Results are probable findings based on observable symptoms, user input, and available live signals.',
  },
  {
    title: 'Replace Professional Inspection',
    description: 'Critical, unsafe, or escalated issues should be checked by a qualified technician.',
  },
];

const actionCategories = [
  {
    label: 'Monitor',
    tone: 'text-blue-400',
    description: 'The symptom is present but not critical. Track frequency and severity before taking action.',
  },
  {
    label: 'Maintain',
    tone: 'text-emerald-400',
    description: 'Routine maintenance may prevent or resolve the issue. No urgent intervention is expected.',
  },
  {
    label: 'Troubleshoot',
    tone: 'text-orange-400',
    description: 'Active investigation is needed. Follow guided steps to narrow down the likely root cause.',
  },
  {
    label: 'Escalate',
    tone: 'text-red-400',
    description: 'The probable cause may require physical inspection or technician support. Back up important data first.',
  },
];

const faqs = [
  {
    question: "How accurate are RigMD's diagnostic results?",
    answer:
      'RigMD provides probable advisory results, not guaranteed diagnoses. Accuracy depends on clear symptom answers, available hardware data, and whether the issue can be inferred without physical inspection.',
  },
  {
    question: 'Does RigMD send my data anywhere?',
    answer:
      'RigMD uses your local app and configured backend services for diagnostic workflows. Avoid entering passwords, private files, or sensitive personal information into free-text fields.',
  },
  {
    question: 'What should I do if my result says Escalate?',
    answer:
      'Treat Escalate as a caution flag. Back up important files, stop risky troubleshooting, and consider a qualified technician if the PC shows power, storage, heat, or display failure symptoms.',
  },
  {
    question: 'Can I use RigMD without completing a System Profile?',
    answer:
      'Yes, but results are better when the System Profile has live hardware data. Missing profile details reduce the context available to the diagnostic engine.',
  },
  {
    question: 'What does Recurring Pattern mean?',
    answer:
      'A recurring pattern means similar symptoms or probable causes appeared across saved diagnostic sessions, which may increase the recommended action level.',
  },
  {
    question: 'How do I interpret the confidence label?',
    answer:
      'Confidence describes how strongly the available answers and system signals support the probable result. It is not a professional confirmation.',
  },
];

function ScopeCard({
  title,
  description,
  supported,
}: {
  title: string;
  description: string;
  supported: boolean;
}) {
  const Icon = supported ? CheckCircle2 : XCircle;
  const iconClass = supported ? 'text-emerald-400' : 'text-red-400';

  return (
    <article className="rounded-xl border border-[#30363d] bg-[#0d1117] px-4 py-3.5">
      <div className="flex items-start gap-3">
        <Icon size={15} className={`mt-0.5 shrink-0 ${iconClass}`} />

        <div>
          <h3 className="text-sm font-bold text-white">{title}</h3>
          <p className="mt-1 text-sm leading-relaxed text-slate-500">{description}</p>
        </div>
      </div>
    </article>
  );
}

export default function HelpScopeView() {
  return (
    <>
      <TopHeader
        title="Help / Scope"
        subtitle="What RigMD can help with, its limitations, and frequently asked questions"
      />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-5 py-5 lg:px-6">
        <div className="mx-auto w-full max-w-[1280px] space-y-8">
          <section className="rounded-2xl border border-[#30363d] bg-[#0d1117] px-5 py-4">
            <div className="flex gap-3">
              <Info size={18} className="mt-0.5 shrink-0 text-cyan-400" />
              <p className="text-sm leading-relaxed text-slate-400">
                RigMD is a <span className="font-bold text-white">symptom-guided diagnostic decision support tool</span>. It helps you
                understand probable causes of observable Windows PC symptoms and recommends one of four action categories:
                Monitor, Maintain, Troubleshoot, or Escalate.
              </p>
            </div>
          </section>

          <section className="grid grid-cols-1 gap-7 xl:grid-cols-2">
            <div>
              <div className="mb-4 flex items-center gap-2">
                <CheckCircle2 size={16} className="text-emerald-400" />
                <h2 className="text-sm font-bold text-white">RigMD Can Help With</h2>
              </div>

              <div className="space-y-3">
                {supportedItems.map((item) => (
                  <ScopeCard key={item.title} supported title={item.title} description={item.description} />
                ))}
              </div>
            </div>

            <div>
              <div className="mb-4 flex items-center gap-2">
                <XCircle size={16} className="text-red-400" />
                <h2 className="text-sm font-bold text-white">RigMD Cannot</h2>
              </div>

              <div className="space-y-3">
                {unsupportedItems.map((item) => (
                  <ScopeCard key={item.title} supported={false} title={item.title} description={item.description} />
                ))}
              </div>
            </div>
          </section>

          <section>
            <div className="mb-4 flex items-center gap-2">
              <Stethoscope size={16} className="text-cyan-400" />
              <h2 className="text-sm font-bold text-white">Understanding Action Categories</h2>
            </div>

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
              {actionCategories.map((category) => (
                <article key={category.label} className="rounded-2xl border border-[#30363d] bg-[#0d1117] p-4">
                  <h3 className={`font-bold ${category.tone}`}>{category.label}</h3>
                  <p className="mt-3 text-sm leading-relaxed text-slate-500">{category.description}</p>
                </article>
              ))}
            </div>
          </section>

          <section className="rounded-2xl border border-[#30363d] bg-[#0d1117] px-5 py-4">
            <div className="flex gap-3">
              <AlertTriangle size={18} className="mt-0.5 shrink-0 text-orange-400" />
              <p className="text-sm leading-relaxed text-slate-400">
                <span className="font-bold text-white">Disclaimer:</span> RigMD provides probable diagnostic advisory only. Results are
                based on observable symptoms, user-reported data, and available live system signals. Always back up important data
                before performing troubleshooting steps.
              </p>
            </div>
          </section>

          <section>
            <div className="mb-4 flex items-center gap-2">
              <BookOpen size={16} className="text-cyan-400" />
              <h2 className="text-sm font-bold text-white">Frequently Asked Questions</h2>
            </div>

            <div className="space-y-2">
              {faqs.map((faq) => (
                <details key={faq.question} className="group rounded-xl border border-[#30363d] bg-[#0d1117] px-4 py-3">
                  <summary className="flex cursor-pointer list-none items-center justify-between gap-4 text-sm font-bold text-white">
                    {faq.question}
                    <ChevronDown size={16} className="shrink-0 text-slate-500 transition group-open:rotate-180" />
                  </summary>
                  <p className="mt-3 border-t border-[#253041] pt-3 text-sm leading-relaxed text-slate-500">{faq.answer}</p>
                </details>
              ))}
            </div>
          </section>

          <p className="pb-4 text-center text-xs text-slate-600">
            RigMD v1.0.0 - Guided PC Diagnostic Decision Support - For Windows desktop users
          </p>
        </div>
      </div>
    </>
  );
}
