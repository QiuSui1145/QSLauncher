import React from "react";
import { Easing, interpolate, spring, useCurrentFrame, useVideoConfig } from "remotion";
import { COLORS, FONT, FONT_MONO } from "../theme";

export const Reveal: React.FC<{
  delay?: number;
  y?: number;
  children: React.ReactNode;
  style?: React.CSSProperties;
}> = ({ delay = 0, y = 26, children, style }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const progress = spring({
    frame: frame - delay,
    fps,
    config: { damping: 200, mass: 0.7, stiffness: 90 },
  });
  const opacity = interpolate(frame - delay, [0, 14], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <div
      style={{
        opacity,
        translate: `0px ${(1 - progress) * y}px`,
        ...style,
      }}
    >
      {children}
    </div>
  );
};

export const Eyebrow: React.FC<{ children: React.ReactNode; color?: string }> = ({
  children,
  color = COLORS.green,
}) => (
  <div
    style={{
      display: "flex",
      alignItems: "center",
      gap: 12,
      fontFamily: FONT_MONO,
      fontSize: 20,
      letterSpacing: "0.22em",
      textTransform: "uppercase",
      color,
    }}
  >
    <span
      style={{
        width: 28,
        height: 1,
        background: color,
        opacity: 0.9,
      }}
    />
    {children}
  </div>
);

export const Headline: React.FC<{
  children: React.ReactNode;
  size?: number;
  style?: React.CSSProperties;
}> = ({ children, size = 78, style }) => (
  <h1
    style={{
      margin: 0,
      fontFamily: FONT,
      fontSize: size,
      lineHeight: 1.12,
      fontWeight: 750,
      letterSpacing: "-0.035em",
      color: COLORS.ink,
      textWrap: "balance",
      ...style,
    }}
  >
    {children}
  </h1>
);

export const Lead: React.FC<{
  children: React.ReactNode;
  size?: number;
  style?: React.CSSProperties;
}> = ({ children, size = 25, style }) => (
  <p
    style={{
      margin: 0,
      fontFamily: FONT,
      fontSize: size,
      lineHeight: 1.75,
      fontWeight: 400,
      color: COLORS.inkSoft,
      maxWidth: 760,
      ...style,
    }}
  >
    {children}
  </p>
);

export const Chip: React.FC<{
  children: React.ReactNode;
  tone?: "green" | "cyan" | "violet" | "plain";
}> = ({ children, tone = "plain" }) => {
  const tint =
    tone === "green"
      ? { color: "#b8f7d4", border: "rgba(110,231,168,0.38)", bg: "rgba(74,222,128,0.10)" }
      : tone === "cyan"
        ? { color: "#c3f0fd", border: "rgba(95,212,244,0.38)", bg: "rgba(56,189,248,0.10)" }
        : tone === "violet"
          ? { color: "#d3d8ff", border: "rgba(154,167,255,0.36)", bg: "rgba(129,140,248,0.10)" }
          : { color: COLORS.inkSoft, border: COLORS.line, bg: "rgba(255,255,255,0.03)" };

  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 8,
        padding: "10px 16px",
        borderRadius: 999,
        border: `1px solid ${tint.border}`,
        background: tint.bg,
        color: tint.color,
        fontFamily: FONT,
        fontSize: 18,
        fontWeight: 550,
        whiteSpace: "nowrap",
      }}
    >
      {children}
    </span>
  );
};

export const Stat: React.FC<{
  value: string;
  label: string;
  accent?: string;
}> = ({ value, label, accent = COLORS.green }) => (
  <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
    <span
      style={{
        fontFamily: FONT,
        fontSize: 58,
        fontWeight: 750,
        letterSpacing: "-0.04em",
        color: accent,
        lineHeight: 1,
      }}
    >
      {value}
    </span>
    <span
      style={{
        fontFamily: FONT,
        fontSize: 17,
        color: COLORS.inkMuted,
        letterSpacing: "0.02em",
      }}
    >
      {label}
    </span>
  </div>
);

export const GlassPanel: React.FC<{
  children: React.ReactNode;
  style?: React.CSSProperties;
}> = ({ children, style }) => (
  <div
    style={{
      background: COLORS.panel,
      border: `1px solid ${COLORS.line}`,
      borderRadius: 26,
      boxShadow:
        "0 30px 80px rgba(0,0,0,0.35), inset 0 1px 0 rgba(255,255,255,0.07)",
      backdropFilter: "blur(14px)",
      ...style,
    }}
  >
    {children}
  </div>
);
