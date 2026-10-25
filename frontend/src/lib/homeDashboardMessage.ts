import type { DashboardSummary } from '../types/rigmd.ts';
import { getCheckNextSteps } from './checkNextSteps.ts';

export function getHomeDashboardMessage(dashboard: DashboardSummary, currentWarnings: string[] = []) {
  const latest = dashboard.last_saved_session ?? dashboard.last_diagnosis ?? dashboard.current_action_status;
  const view = latest ? getCheckNextSteps(latest) : null;
  if (dashboard.database_warning) return { latest, primary: 'new', title: 'Check history is unavailable', subtitle: 'Saved results could not be loaded. Current readings are separate from saved check history.' };
  if (dashboard.warning_signs_active_count > 0) return { latest, primary: 'alerts', title: 'You have saved alerts to review', subtitle: 'Review the recorded alerts before choosing a next step.' };
  if (currentWarnings.length) return { latest, primary: 'current', title: 'Current readings need review', subtitle: `Readings to review: ${currentWarnings.join(', ')}. Run a relevant check before deciding on changes.` };
  if (view?.attention.length && latest?.action_category.toLowerCase() === 'escalate') return { latest, primary: 'latest', title: 'Your latest check recommends professional inspection', subtitle: 'Review the saved warning and recommended inspection steps before attempting changes.' };
  if (view?.attention.length) return { latest, primary: 'latest', title: 'Your latest saved check has warnings to review', subtitle: 'Open the saved findings before choosing an action. A warning alone does not establish its cause.' };
  if (view?.unavailable.length) return { latest, primary: 'latest', title: 'Your latest check was limited', subtitle: 'Some checks were not verified. Review the saved findings; this is not an overall health result.' };
  if (latest) return { latest, primary: 'latest', title: 'Your latest check is ready to review', subtitle: 'Review what was checked and its saved evidence. No warning in those readings does not rule out every problem.' };
  return { latest, primary: 'new', title: 'Ready for your first device check', subtitle: 'Choose a full check, specific parts, or a problem you have noticed.' };
}
