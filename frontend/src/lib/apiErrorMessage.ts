function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' ? value as Record<string, unknown> : {};
}

function message(value: unknown): string {
  if (typeof value === 'string') return value;
  const row = record(value);
  return typeof row.msg === 'string' ? row.msg : typeof row.message === 'string' ? row.message : '';
}

export function getApiErrorMessage(error: unknown, fallback: string): string {
  const row = record(error);
  const detail = record(record(row.response).data).detail;
  const text = (Array.isArray(detail) ? detail.map(message).filter(Boolean).join(' ') : message(detail)) || message(error) || fallback;
  return /at\s+\S+\s+\(|Traceback|System\./.test(text) ? fallback : text;
}
