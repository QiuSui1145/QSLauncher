import React from "react";
import { AbsoluteFill, interpolate, spring, useCurrentFrame, useVideoConfig } from "remotion";
import { Eyebrow, Headline, Lead, Chip, GlassPanel, Reveal } from "../components/Type";
import { Logo } from "../components/Logo";
import { COLORS, FONT, FONT_MONO } from "../theme";

const Node: React.FC<{
  label: string;
  sub: string;
  x: number;
  y: number;
  color: string;
  delay: number;
}> = ({ label, sub, x, y, color, delay }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const progress = spring({
    frame: frame - delay,
    fps,
    config: { damping: 180, mass: 0.8, stiffness: 90 },
  });
  const pulse = 0.5 + Math.sin((frame + delay) / 20) * 0.5;

  return (
    <div
      style={{
        position: "absolute",
        left: x,
        top: y,
        translate: "-50% -50%",
        opacity: progress,
        scale: 0.82 + progress * 0.18,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: 10,
      }}
    >
      <div
        style={{
          width: 118,
          height: 118,
          borderRadius: 36,
          background: "rgba(8,16,20,0.88)",
          border: `1px solid ${color}55`,
          boxShadow: `0 0 ${20 + pulse * 24}px ${color}22, inset 0 1px 0 rgba(255,255,255,0.08)`,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          color,
          fontFamily: FONT,
          fontSize: 24,
          fontWeight: 700,
          letterSpacing: "-0.02em",
        }}
      >
        {label}
      </div>
      <div
        style={{
          color: COLORS.inkMuted,
          fontFamily: FONT_MONO,
          fontSize: 13,
          letterSpacing: "0.08em",
          textAlign: "center",
          whiteSpace: "nowrap",
        }}
      >
        {sub}
      </div>
    </div>
  );
};

export const PrincipleScene: React.FC = () => {
  const frame = useCurrentFrame();
  const dash = interpolate(frame, [0, 180], [0, -48]);
  const centerPulse = 0.5 + Math.sin(frame / 18) * 0.5;

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 100,
          top: 158,
          width: 760,
          display: "flex",
          flexDirection: "column",
          gap: 26,
        }}
      >
        <Reveal>
          <Eyebrow color={COLORS.cyan}>OPEN BY DESIGN</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={70}>
            去中心化，
            <br />
            不经过任何中间服务器
          </Headline>
        </Reveal>
        <Reveal delay={20}>
          <Lead>
            启动器只负责把客户端跑起来。
            <br />
            没有启动器账号，没有中转下载，没有遥测。
          </Lead>
        </Reveal>
        <Reveal delay={32}>
          <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
            <Chip tone="green">不代理下载</Chip>
            <Chip tone="cyan">不收集数据</Chip>
            <Chip tone="violet">不锁定账号</Chip>
          </div>
        </Reveal>

        <Reveal delay={46}>
          <div
            style={{
              display: "flex",
              alignItems: "flex-start",
              gap: 14,
              marginTop: 8,
              paddingTop: 22,
              borderTop: `1px solid ${COLORS.line}`,
            }}
          >
            <span
              style={{
                fontFamily: FONT_MONO,
                fontSize: 13,
                color: COLORS.green,
                border: "1px solid rgba(110,231,168,0.35)",
                borderRadius: 7,
                padding: "5px 8px",
                letterSpacing: "0.08em",
              }}
            >
              SOURCE
            </span>
            <p
              style={{
                margin: 0,
                fontFamily: FONT,
                fontSize: 18,
                lineHeight: 1.7,
                color: COLORS.inkSoft,
                maxWidth: 600,
              }}
            >
              所有下载相关功能都直接请求
              <span style={{ color: COLORS.ink, fontWeight: 650 }}> 官方源 </span>
              ：Mojang 版本清单与资源、Modrinth API、Fabric / Forge / NeoForge / Quilt
              官方元数据。
            </p>
          </div>
        </Reveal>
      </div>

      <div style={{ position: "absolute", right: 72, top: 150 }}>
        <GlassPanel
          style={{
            width: 880,
            height: 700,
            position: "relative",
            overflow: "hidden",
          }}
        >
          <div
            style={{
              position: "absolute",
              inset: 0,
              backgroundImage:
                "radial-gradient(rgba(255,255,255,0.10) 1px, transparent 1px)",
              backgroundSize: "26px 26px",
              opacity: 0.5,
            }}
          />

          <svg width="880" height="700" style={{ position: "absolute", inset: 0 }}>
            {[
              { x1: 440, y1: 350, x2: 180, y2: 210, delay: 0 },
              { x1: 440, y1: 350, x2: 700, y2: 190, delay: 10 },
              { x1: 440, y1: 350, x2: 660, y2: 540, delay: 20 },
            ].map((line, i) => (
              <line
                key={i}
                x1={line.x1}
                y1={line.y1}
                x2={line.x2}
                y2={line.y2}
                stroke={i === 1 ? "rgba(95,212,244,0.45)" : "rgba(110,231,168,0.40)"}
                strokeWidth="1.5"
                strokeDasharray="7 9"
                strokeDashoffset={dash - line.delay}
              />
            ))}
          </svg>

          <div
            style={{
              position: "absolute",
              left: 440,
              top: 350,
              translate: "-50% -50%",
              width: 170,
              height: 170,
              borderRadius: 50,
              background:
                "radial-gradient(circle at 35% 30%, rgba(110,231,168,0.30), rgba(8,16,20,0.92) 70%)",
              border: "1px solid rgba(110,231,168,0.55)",
              boxShadow: `0 0 ${30 + centerPulse * 30}px rgba(74,222,128,0.25)`,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              flexDirection: "column",
              gap: 6,
            }}
          >
            <Logo size={52} />
            <span
              style={{
                fontFamily: FONT_MONO,
                fontSize: 12,
                letterSpacing: "0.18em",
                color: COLORS.green,
              }}
            >
              LOCAL
            </span>
          </div>

          <Node label="Mojang" sub="版本清单 / 资源" x={180} y={210} color={COLORS.green} delay={16} />
          <Node label="Modrinth" sub="官方 API" x={700} y={190} color={COLORS.cyan} delay={24} />
          <Node label="Loaders" sub="Fabric · Forge · Quilt" x={660} y={540} color={COLORS.violet} delay={32} />

          <div
            style={{
              position: "absolute",
              left: 30,
              bottom: 26,
              fontFamily: FONT_MONO,
              fontSize: 12,
              letterSpacing: "0.14em",
              color: COLORS.inkMuted,
            }}
          >
            DIRECT CONNECTIONS · NO PROXY
          </div>
        </GlassPanel>
      </div>
    </AbsoluteFill>
  );
};
