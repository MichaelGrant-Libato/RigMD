// The server owns model, request-size, and output-cost bounds.
export const REQUEST_BYTES = 128 * 1024;
export const MODELS = new Set(['gemini-3.5-flash', 'gemini-3.8-flash', 'gemini-2.5-flash']);
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const name = value => typeof value === 'string' && /^[a-z][a-z0-9_]{0,63}$/.test(value);
const keys = (value, allowed) => object(value) && Object.keys(value).every(key => allowed.includes(key));

export function validatePayload(payload, model) {
  if (!MODELS.has(model) || !keys(payload, ['contents', 'systemInstruction', 'tools', 'toolConfig', 'generationConfig'])) return null;
  if (!Array.isArray(payload.contents) || payload.contents.length < 1 || payload.contents.length > 40) return null;
  const validParts = parts => Array.isArray(parts) && parts.length > 0 && parts.length <= 32 && parts.every(part => {
    if (!object(part) || Object.keys(part).length !== 1) return false;
    if ('text' in part) return typeof part.text === 'string' && part.text.length <= 65536;
    if ('functionCall' in part) return keys(part.functionCall, ['name', 'args', 'id']) && name(part.functionCall.name) && object(part.functionCall.args);
    if ('functionResponse' in part) return keys(part.functionResponse, ['name', 'response', 'id']) && name(part.functionResponse.name) && object(part.functionResponse.response);
    return false;
  });
  if (!payload.contents.every(content => keys(content, ['role', 'parts']) &&
    (content.role === undefined || ['user', 'model'].includes(content.role)) && validParts(content.parts))) return null;
  if (payload.systemInstruction !== undefined &&
    (!keys(payload.systemInstruction, ['parts', 'role']) || !Array.isArray(payload.systemInstruction.parts) ||
      !payload.systemInstruction.parts.every(part => keys(part, ['text']) && typeof part.text === 'string') ||
      !validParts(payload.systemInstruction.parts))) return null;
  if (payload.tools !== undefined && (!Array.isArray(payload.tools) || payload.tools.length > 1 ||
    !payload.tools.every(tool => keys(tool, ['functionDeclarations']) && Array.isArray(tool.functionDeclarations) &&
      tool.functionDeclarations.length <= 32 && tool.functionDeclarations.every(declaration =>
        keys(declaration, ['name', 'description', 'parameters']) && name(declaration.name) &&
        (declaration.description === undefined || typeof declaration.description === 'string') &&
        (declaration.parameters === undefined || object(declaration.parameters)))))) return null;
  if (payload.toolConfig !== undefined && (!keys(payload.toolConfig, ['functionCallingConfig']) ||
    !keys(payload.toolConfig.functionCallingConfig, ['mode', 'allowedFunctionNames']) ||
    !['AUTO', 'ANY', 'NONE', 'VALIDATED'].includes(payload.toolConfig.functionCallingConfig.mode) ||
    (payload.toolConfig.functionCallingConfig.allowedFunctionNames !== undefined &&
      (!Array.isArray(payload.toolConfig.functionCallingConfig.allowedFunctionNames) ||
        !payload.toolConfig.functionCallingConfig.allowedFunctionNames.every(name))))) return null;
  const generation = payload.generationConfig ?? {};
  if (!keys(generation, ['temperature', 'maxOutputTokens', 'thinkingConfig', 'candidateCount', 'responseMimeType'])) return null;
  if (generation.maxOutputTokens !== undefined && (!Number.isInteger(generation.maxOutputTokens) || generation.maxOutputTokens < 1)) return null;
  if (generation.temperature !== undefined && (typeof generation.temperature !== 'number' || generation.temperature < 0 || generation.temperature > 2)) return null;
  if (generation.responseMimeType !== undefined && !['text/plain', 'application/json'].includes(generation.responseMimeType)) return null;
  return { ...payload, generationConfig: { temperature: generation.temperature ?? 0.2,
    maxOutputTokens: Math.min(generation.maxOutputTokens ?? 2048, 2048), candidateCount: 1,
    ...(generation.responseMimeType ? { responseMimeType: generation.responseMimeType } : {}),
    thinkingConfig: model === 'gemini-2.5-flash' ? { thinkingBudget: 0 } : { thinkingLevel: model === 'gemini-3.8-flash' ? 'low' : 'minimal' } } };
}
