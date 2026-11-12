import { createContext } from 'react';

export const RemediationReviewAccess = createContext({ allowed: false, message: 'Action eligibility has not been verified.', sessionId: '' });
