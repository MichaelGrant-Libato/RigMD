import type { Variants } from 'motion/react';

export const pageFade: Variants = {
  hidden: { opacity: 0, y: 10 },
  visible: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: 6 },
};

export const cardFadeUp: Variants = {
  hidden: { opacity: 0, y: 12 },
  visible: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: 8 },
};

export const staggerContainer: Variants = {
  hidden: {},
  visible: {
    transition: {
      staggerChildren: 0.05,
    },
  },
};

export const drawerSlideRight: Variants = {
  hidden: { opacity: 0, x: 24 },
  visible: { opacity: 1, x: 0 },
  exit: { opacity: 0, x: 24 },
};

export const accordionReveal: Variants = {
  hidden: { height: 0, opacity: 0 },
  visible: { height: 'auto', opacity: 1 },
  exit: { height: 0, opacity: 0 },
};

export const fadeIn: Variants = {
  hidden: { opacity: 0 },
  visible: { opacity: 1 },
  exit: { opacity: 0 },
};

export const pageTransition = {
  duration: 0.25,
  ease: 'easeOut',
} as const;

export const cardTransition = {
  duration: 0.25,
  ease: 'easeOut',
} as const;

export const hoverLift = {
  y: -2,
  scale: 1.01,
} as const;

export const buttonTap = {
  scale: 0.98,
} as const;
