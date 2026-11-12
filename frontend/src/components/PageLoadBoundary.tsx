import { Component, type ReactNode } from 'react';

export default class PageLoadBoundary extends Component<{ children: ReactNode; onHome: () => void }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() {
    if (!this.state.failed) return this.props.children;
    return <section role="alert" className="m-6 rounded-xl border border-amber-400/30 bg-[var(--rigmd-card)] p-6">
      <h1 className="text-xl font-bold text-white">This page could not be opened</h1>
      <p className="mt-3 text-sm text-slate-300">Return to Home and try again. If the problem continues, restart RigMD. Your saved checks remain available.</p>
      <button type="button" onClick={this.props.onHome} className="mt-4 rounded-lg border border-cyan-400/40 px-4 py-2 text-cyan-200">Return to Home</button>
    </section>;
  }
}
