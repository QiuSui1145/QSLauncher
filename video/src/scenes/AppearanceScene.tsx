import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame } from "remotion";
import { ShotCard } from "../components/Shot";
import { Eyebrow, Headline, Lead, Chip, Reveal } from "../components/Type";
import { COLORS, FONT, FONT_MONO } from "../theme";

const Feature: React.FC<{ title: string; text: string; delay: number }> = ({
  title,
  text,
  delay,
}) => (
  <Reveal delay={delay} y={18}>
    <div
      style={{
        padding: "16px 18px",
        borderRadius: 15,
        border: `1px solid ${COLORS.line}`,
        background: "rgba(8,16,20,0.5)",
        display: "flex",
        flexDirection: "column",
        gap: 5,
      }}
    >
      <span style={{ fontFamily: FONT, fontSize: 18, fontWeight: 650, color: COLORS.ink }}>
        {title}
      </span>
      <span style={{ fontFamily: FONT, fontSize: 14, lineHeight: 1.5, color: COLORS.inkMuted }}>
        {text}
      </span>
    </div>
  </Reveal>
);

export const AppearanceScene: React.FC = () => {
  const frame = useCurrentFrame();

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 96,
          top: 108,
          width: 450,
          display: "flex",
          flexDirection: "column",
          gap: 18,
        }}
      >
        <Reveal>
          <Eyebrow color={COLORS.green}>APPEARANCE</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={52}>
            你的启动器，
            <br />
            应该像你的
          </Headline>
        </Reveal>
        <Reveal delay={18}>
          <Lead size={17}>
            深色、浅色或跟随系统；强调色、背景图、磨砂强度都能单独调整。
          </Lead>
        </Reveal>
        <div style={{ display: "flex", gap: 9, flexWrap: "wrap", marginBottom: 4 }}>
          <Chip tone="green">磨砂菜单</Chip>
          <Chip tone="cyan">自定义背景</Chip>
        </div>
        <Feature title="主题与强调色" text="实时预览，文字对比度自动校正。" delay={30} />
        <Feature title="图片 / 视频背景" text="0–40 级磨砂，面板始终比菜单更实。" delay={38} />
        <Feature title="多账户入口" text="微软正版、离线、LittleSkin / Yggdrasil。" delay={46} />
      </div>

      <div style={{ position: "absolute", left: 590, top: 104 }}>
        <div
          style={{
            opacity: interpolate(frame, [4, 22], [0, 1], {
              extrapolateLeft: "clamp",
              extrapolateRight: "clamp",
            }),
          }}
        >
          <ShotCard
            src="shots/settings.png"
            width={1270}
            tilt={-1}
            glow="rgba(74,222,128,0.20)"
            label="REAL UI · SETTINGS"
          />
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          left: 620,
          bottom: 54,
          display: "flex",
          gap: 10,
          fontFamily: FONT_MONO,
          fontSize: 11,
          letterSpacing: "0.12em",
          color: COLORS.inkMuted,
        }}
      >
        <span>THEME</span>
        <span style={{ opacity: 0.4 }}>/</span>
        <span>WALLPAPER</span>
        <span style={{ opacity: 0.4 }}>/</span>
        <span>FROSTED GLASS</span>
      </div>
    </AbsoluteFill>
  );
};
