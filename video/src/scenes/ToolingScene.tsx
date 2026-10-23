import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame } from "remotion";
import { ShotCard } from "../components/Shot";
import { Eyebrow, Headline, Lead, Reveal, Stat } from "../components/Type";
import { COLORS, FONT_MONO } from "../theme";

export const ToolingScene: React.FC = () => {
  const frame = useCurrentFrame();

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 96,
          top: 82,
          width: 900,
          display: "flex",
          flexDirection: "column",
          gap: 16,
        }}
      >
        <Reveal>
          <Eyebrow color={COLORS.cyan}>TOOLING</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={52}>Java、空间、日志，全都管好</Headline>
        </Reveal>
        <Reveal delay={18}>
          <Lead size={18} style={{ maxWidth: 820 }}>
            官方运行时一键安装，扫描本机 Java；所有数据留在 data/，日志可打包成 ZIP 发给别人排查。
          </Lead>
        </Reveal>
      </div>

      <div
        style={{
          position: "absolute",
          left: 82,
          top: 350,
          opacity: interpolate(frame, [4, 22], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
          rotate: "-2.2deg",
        }}
      >
        <ShotCard
          src="shots/java.png"
          width={780}
          tilt={0}
          glow="rgba(74,222,128,0.20)"
          label="JAVA RUNTIME"
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 730,
          top: 322,
          opacity: interpolate(frame, [14, 32], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
          rotate: "1.8deg",
        }}
      >
        <ShotCard
          src="shots/resources.png"
          width={650}
          tilt={0}
          glow="rgba(95,212,244,0.22)"
          label="PORTABLE DATA"
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 1270,
          top: 420,
          opacity: interpolate(frame, [24, 42], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
          rotate: "2.4deg",
        }}
      >
        <ShotCard
          src="shots/logs.png"
          width={560}
          tilt={0}
          glow="rgba(154,167,255,0.20)"
          label="LAUNCH LOGS"
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 96,
          bottom: 88,
          display: "flex",
          gap: 92,
        }}
      >
        <Reveal delay={44}>
          <Stat value="5" label="本机 Java 运行时" />
        </Reveal>
        <Reveal delay={50}>
          <Stat value="data/" label="所有数据随 exe 走" accent={COLORS.cyan} />
        </Reveal>
        <Reveal delay={56}>
          <Stat value="ZIP" label="完整日志一键导出" accent={COLORS.violet} />
        </Reveal>
      </div>

      <div
        style={{
          position: "absolute",
          right: 100,
          bottom: 58,
          fontFamily: FONT_MONO,
          fontSize: 12,
          letterSpacing: "0.14em",
          color: COLORS.inkMuted,
        }}
      >
        NO REGISTRY · NO INSTALLER
      </div>
    </AbsoluteFill>
  );
};
