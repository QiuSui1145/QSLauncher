import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame } from "remotion";
import { ShotCard } from "../components/Shot";
import { Eyebrow, Headline, Lead, Chip, Reveal, Stat } from "../components/Type";
import { COLORS, FONT, FONT_MONO } from "../theme";

export const VersionsScene: React.FC = () => {
  const frame = useCurrentFrame();

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 96,
          top: 110,
          width: 500,
          display: "flex",
          flexDirection: "column",
          gap: 20,
        }}
      >
        <Reveal>
          <Eyebrow>VERSIONS & INSTANCES</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={54}>
            版本、加载器、实例
            <br />
            一处管理
          </Headline>
        </Reveal>
        <Reveal delay={18}>
          <Lead size={18}>
            从 Mojang 官方清单读取全部版本；每个实例拥有独立的
            mods、config、saves 与 Java 设置，互不污染。
          </Lead>
        </Reveal>
        <Reveal delay={28}>
          <div style={{ display: "flex", gap: 9, flexWrap: "wrap" }}>
            {["Fabric", "Forge", "NeoForge", "Quilt", "原版"].map((item) => (
              <Chip key={item}>{item}</Chip>
            ))}
          </div>
        </Reveal>
        <Reveal delay={38}>
          <div style={{ display: "flex", gap: 46, marginTop: 14 }}>
            <Stat value="920" label="官方版本已同步" />
            <Stat value="6" label="隔离实例" accent={COLORS.cyan} />
          </div>
        </Reveal>
      </div>

      <div style={{ position: "absolute", left: 610, top: 118 }}>
        <div
          style={{
            opacity: interpolate(frame, [4, 22], [0, 1], {
              extrapolateLeft: "clamp",
              extrapolateRight: "clamp",
            }),
          }}
        >
          <ShotCard
            src="shots/versions.png"
            width={1230}
            tilt={-1.2}
            glow="rgba(74,222,128,0.20)"
            label="REAL UI · VERSIONS"
          />
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          left: 86,
          top: 650,
          opacity: interpolate(frame, [18, 38], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
          translate: `0px ${interpolate(frame, [18, 40], [30, 0], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          })}px`,
        }}
      >
        <ShotCard
          src="shots/downloads.png"
          width={505}
          tilt={1.8}
          glow="rgba(95,212,244,0.22)"
          radius={16}
          label="MOJANG · 官方清单"
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 620,
          bottom: 60,
          display: "flex",
          alignItems: "center",
          gap: 10,
          fontFamily: FONT,
          fontSize: 16,
          color: COLORS.inkMuted,
        }}
      >
        <span
          style={{
            width: 7,
            height: 7,
            borderRadius: 99,
            background: COLORS.green,
            boxShadow: `0 0 12px ${COLORS.green}`,
          }}
        />
        下载直连 Mojang 官方 CDN，SHA-1 校验后落盘
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
        INSTANCE ISOLATION
      </div>
    </AbsoluteFill>
  );
};
