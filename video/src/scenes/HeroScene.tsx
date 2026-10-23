import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame } from "remotion";
import { ShotCard } from "../components/Shot";
import { Reveal, Eyebrow, Headline, Lead, Chip, GlassPanel } from "../components/Type";
import { Logo } from "../components/Logo";
import { COLORS, FONT, FONT_MONO } from "../theme";

export const HeroScene: React.FC = () => {
  const frame = useCurrentFrame();
  const float = Math.sin(frame / 42) * 8;

  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", left: 96, top: 74 }}>
        <Reveal delay={2}>
          <Logo size={60} withWordmark />
        </Reveal>
      </div>

      <div
        style={{
          position: "absolute",
          left: 100,
          top: 248,
          width: 760,
          display: "flex",
          flexDirection: "column",
          gap: 30,
        }}
      >
        <Reveal delay={10}>
          <Eyebrow>MINECRAFT JAVA · 1.2.0</Eyebrow>
        </Reveal>

        <Reveal delay={18}>
          <Headline size={98} style={{ lineHeight: 1.08 }}>
            把 Minecraft
            <br />
            <span
              style={{
                background:
                  "linear-gradient(100deg, #f2f7f8 10%, #6ee7a8 55%, #5fd4f4 95%)",
                WebkitBackgroundClip: "text",
                color: "transparent",
              }}
            >
              交还给你
            </span>
          </Headline>
        </Reveal>

        <Reveal delay={30}>
          <Lead>
            一个完全便携、去中心化的启动器。
            <br />
            不登录中间账号，不代理下载，不收集数据。
          </Lead>
        </Reveal>

        <Reveal delay={40}>
          <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
            <Chip tone="green">完全便携</Chip>
            <Chip tone="cyan">去中心化</Chip>
            <Chip tone="violet">官方源</Chip>
          </div>
        </Reveal>
      </div>

      <div
        style={{
          position: "absolute",
          left: 880,
          top: 132 + float,
          width: 1180,
        }}
      >
        <div
          style={{
            opacity: interpolate(frame, [8, 34], [0, 1], {
              extrapolateLeft: "clamp",
              extrapolateRight: "clamp",
            }),
            translate: `${interpolate(frame, [8, 40], [48, 0], {
              extrapolateLeft: "clamp",
              extrapolateRight: "clamp",
            })}px 0px`,
          }}
        >
          <ShotCard src="shots/home.png" width={1180} tilt={-3.2} glow="rgba(74,222,128,0.26)" />
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          right: 92,
          bottom: 116,
          display: "flex",
          alignItems: "center",
          gap: 14,
          padding: "12px 18px",
          borderRadius: 14,
          background: "rgba(7,14,18,0.82)",
          border: `1px solid ${COLORS.line}`,
          opacity: interpolate(frame, [36, 52], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
        }}
      >
        <span
          style={{
            width: 8,
            height: 8,
            borderRadius: 99,
            background: COLORS.green,
            boxShadow: `0 0 14px ${COLORS.green}`,
          }}
        />
        <span
          style={{
            fontFamily: FONT_MONO,
            fontSize: 14,
            letterSpacing: "0.12em",
            color: COLORS.inkSoft,
          }}
        >
          REAL UI · 纯净演示数据
        </span>
      </div>

      <div
        style={{
          position: "absolute",
          left: 100,
          bottom: 116,
          fontFamily: FONT,
          fontSize: 15,
          color: COLORS.inkMuted,
          opacity: interpolate(frame, [48, 64], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
        }}
      >
        Windows · 单文件便携版 · 无需安装
      </div>
    </AbsoluteFill>
  );
};
