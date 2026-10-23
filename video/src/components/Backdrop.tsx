import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig } from "remotion";
import { COLORS } from "../theme";

/**
 * Persistent background: slow aurora, technical grid and film grain.
 * It lives outside the scenes so cuts never flash to pure black.
 */
export const Backdrop: React.FC = () => {
  const frame = useCurrentFrame();
  const { durationInFrames } = useVideoConfig();
  const drift = interpolate(frame, [0, durationInFrames], [0, 1]);
  const breathe = Math.sin(frame / 90) * 0.5 + 0.5;

  return (
    <AbsoluteFill style={{ backgroundColor: COLORS.bg, overflow: "hidden" }}>
      <div
        style={{
          position: "absolute",
          inset: "-20%",
          background: `
            radial-gradient(46% 42% at ${18 + drift * 6}% ${16 + Math.sin(frame / 120) * 3}%, rgba(74,222,128,0.26), transparent 68%),
            radial-gradient(38% 40% at ${80 - drift * 5}% ${24 + Math.cos(frame / 150) * 4}%, rgba(56,189,248,0.22), transparent 70%),
            radial-gradient(42% 44% at ${58 + Math.sin(frame / 200) * 4}% ${88 - drift * 3}%, rgba(129,140,248,0.16), transparent 72%),
            linear-gradient(140deg, #05080d 0%, #071018 55%, #041014 100%)
          `,
        }}
      />

      <div
        style={{
          position: "absolute",
          inset: 0,
          opacity: 0.5,
          backgroundImage:
            "linear-gradient(rgba(255,255,255,0.035) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.035) 1px, transparent 1px)",
          backgroundSize: "80px 80px",
          backgroundPosition: `${drift * 40}px ${drift * 40}px`,
          maskImage: "radial-gradient(80% 70% at 50% 40%, black, transparent 85%)",
        }}
      />

      <div
        style={{
          position: "absolute",
          inset: 0,
          opacity: 0.35,
          backgroundImage:
            "radial-gradient(1px 1px at 12% 18%, rgba(255,255,255,0.6), transparent), radial-gradient(1px 1px at 28% 62%, rgba(255,255,255,0.35), transparent), radial-gradient(1px 1px at 73% 31%, rgba(255,255,255,0.5), transparent), radial-gradient(1px 1px at 88% 74%, rgba(255,255,255,0.3), transparent), radial-gradient(1px 1px at 54% 88%, rgba(255,255,255,0.4), transparent)",
          translate: `${Math.sin(frame / 110) * 6}px ${Math.cos(frame / 140) * 5}px`,
        }}
      />

      <div
        style={{
          position: "absolute",
          inset: 0,
          background:
            "radial-gradient(110% 90% at 50% 45%, transparent 42%, rgba(0,0,0,0.55) 100%)",
        }}
      />

      <div
        style={{
          position: "absolute",
          inset: 0,
          opacity: 0.05 + breathe * 0.02,
          backgroundImage:
            "url(\"data:image/svg+xml,%3Csvg viewBox='0 0 160 160' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E\")",
        }}
      />
    </AbsoluteFill>
  );
};
