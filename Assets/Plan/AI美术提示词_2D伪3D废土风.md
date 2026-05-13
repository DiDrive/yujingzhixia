# AI 美术提示词手册（2D/伪3D 写实旧工业废土）

## 1. 风格基线（统一要求）
- 视角：手绘 `45°斜俯视`（非严格等角）
- 光照：左上主光，右下软阴影
- 风格：写实旧工业废土，中等细节，外轮廓线略粗、内线略细
- 配色：低饱和灰褐/军绿/铁锈红，点缀色用警示黄（小面积）
- 材质：锈蚀、磨损、污渍、刮痕，但保持可读性
- 输出：透明背景 PNG，禁止水印、禁止文字、禁止边框

---

## 2. 通用主提示词（可复用模板）
```text
A 2D pseudo-3D game asset in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, consistent left-top key light and soft shadow to bottom-right, clean readable silhouette, medium detail, worn metal, rust, scratches, grime, muted low-saturation palette (grey, brown, olive) with small hazard-yellow accents, transparent background, no text, no watermark, no border, high readability for game UI and gameplay.
```

## 3. 通用负面提示词（建议每次都带）
```text
blurry, lowres, noisy background, photorealistic photo, 3D render look, over-saturated colors, anime style, chibi, extra objects, duplicate object, cut off, cropped, text, logo, watermark, frame, border, white background, black background, messy perspective, inconsistent lighting
```

---

## 4. 物品图标提示词（批量）

### 4.1 基础图标模板
```text
{通用主提示词}
Asset type: item icon.
Object: {物品描述}.
Composition: centered single object, enough padding, icon-ready.
Resolution: 512x512.
```

### 4.2 已填好的物品清单（直接可用）

#### 1) 罐头 `food_canned`
```text
A 2D pseudo-3D item icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, consistent left-top key light and soft shadow to bottom-right, medium detail, worn tin can with faded label and slight rust, muted palette with tiny hazard-yellow accent, centered composition, transparent background, no text, no watermark, no border, 512x512
```

#### 2) 瓶装水 `water_bottle`
```text
A 2D pseudo-3D item icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, worn plastic water bottle with scuffed cap and partially torn label, subtle dirt marks, left-top light and soft right-bottom shadow, centered composition, transparent background, no text, no watermark, no border, 512x512
```

#### 3) 绷带 `bandage`
```text
A 2D pseudo-3D item icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, rolled medical bandage with cloth texture, slightly stained and worn package strip, muted colors, clear silhouette, transparent background, no text, no watermark, no border, 512x512
```

#### 4) 布料 `cloth_raw`
```text
A 2D pseudo-3D item icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, folded rough cloth bundle tied with simple string, frayed edges, dusty and worn, muted palette, transparent background, no text, no watermark, no border, 512x512
```

#### 5) 9mm 弹药 `ammo_9mm`
```text
A 2D pseudo-3D item icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, small box of 9mm rounds with several bullets visible, scratched metal tips, worn cardboard pack, low saturation colors, transparent background, no text, no watermark, no border, 512x512
```

#### 6) 门禁卡 `keycard`
```text
A 2D pseudo-3D item icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, worn access keycard with chipped corner and dirty stripe, slight hazard-yellow stripe detail, clean silhouette, transparent background, no text, no watermark, no border, 512x512
```

---

## 5. 场景道具提示词（伪3D 多视角）

### 5.1 六视角道具模板
```text
A 2D pseudo-3D prop sheet, same object shown in 6 angles (front-left, front, front-right, left, right, back-right), hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, consistent left-top lighting and soft right-bottom shadow, medium detail, transparent background, no text, no watermark, no border.
Prop: {道具描述}
```

### 5.2 发电机（示例）
```text
A 2D pseudo-3D prop sheet, same diesel generator shown in 6 angles (front-left, front, front-right, left, right, back-right), hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, old cylindrical tanks, hoses, gauges, worn control box, rust, grime, muted palette with tiny hazard-yellow accent, consistent left-top light and soft right-bottom shadow, transparent background, no text, no watermark, no border
```

### 5.3 工作台（示例）
```text
A 2D pseudo-3D prop sheet, same survival workbench shown in 6 angles, hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, cluttered tools, vise, metal surface, worn wood and steel parts, scratches and oil stains, muted low-saturation colors, transparent background, no text, no watermark, no border
```

---

## 6. UI 套件提示词（背包/网格）

### 6.1 UI 套件总提示词
```text
Game UI kit for a post-apocalyptic survival inventory, 2D hand-drawn style with pseudo-3D shading, realistic old-industrial look, dark metal panel, bolts, scratches, muted colors with small hazard-yellow accents, high readability.
Include separated elements: panel background, slot cell (empty/hover/occupied/valid/invalid), tab button (normal/hover/pressed/active), action button (normal/hover/pressed), scrollbar, simple divider lines.
Transparent background for each element, no text, no watermark, no border.
```

### 6.2 单元素提示词
- `ui_inv_panel`
```text
A 2D game UI panel background, old-industrial post-apocalyptic style, dark brushed metal, corner bolts, subtle rust and scratches, hazard-yellow tiny accents, rectangular with clean edges, transparent background, no text, no watermark, no border, 1024x1024
```

- `ui_cell_empty`
```text
A 2D inventory grid slot, empty state, dark metal frame with subtle depth, hand-drawn old-industrial style, transparent background, no text, no watermark, no border, 256x256
```

- `ui_cell_occupied`
```text
A 2D inventory grid slot, occupied state, slightly brighter inner tint and subtle blue-grey fill, old-industrial hand-drawn style, transparent background, no text, no watermark, no border, 256x256
```

- `ui_cell_valid`
```text
A 2D inventory placement overlay cell, valid state, translucent green highlight, clean readable shape, transparent background, no text, no watermark, no border, 256x256
```

- `ui_cell_invalid`
```text
A 2D inventory placement overlay cell, invalid state, translucent red highlight, clean readable shape, transparent background, no text, no watermark, no border, 256x256
```

---

## 7. 角色提示词（玩家）

### 7.1 玩家立绘（单视角）
```text
A full-body 2D pseudo-3D survivor character in hand-drawn 45-degree oblique top-down game style, realistic old-industrial post-apocalyptic mood, consistent left-top light and soft right-bottom shadow, rugged jacket, layered utility vest, worn backpack straps, practical boots, muted low-saturation colors with tiny hazard-yellow accent, clean silhouette, transparent background, no text, no watermark, no border, 1024x1024
```

### 7.2 玩家八方向行走（分开提示词，纯色背景）
- 说明：以下每条用于单独生成一个方向的行走序列，背景统一纯色（建议 `#7A7A7A` 灰色）便于后期抠图与对齐。

- `N（向上）`
```text
A 2D pseudo-3D character walk sprite sheet, post-apocalyptic survivor, walking up (North / up on screen), back view (facing away from camera), 4 walking frames, hand-drawn 45-degree oblique top-down game style, fixed camera angle across directions, character upright, no tilted camera, no dutch angle, consistent body proportions and outfit details, left-top lighting, solid flat background color #7A7A7A, no texture background, no text, no watermark, no border
```

- `NE（右上）`
```text
A 2D pseudo-3D character walk sprite sheet, post-apocalyptic survivor, walking up-right (North-East / up-right on screen), 3/4 back view, 4 walking frames, hand-drawn 45-degree oblique top-down game style, fixed camera angle across directions, character upright, no tilted camera, no dutch angle, consistent body proportions and outfit details, left-top lighting, solid flat background color #7A7A7A, no texture background, no text, no watermark, no border
```

- `E（向右）`
```text
A 2D pseudo-3D character walk sprite sheet, post-apocalyptic survivor, walking right (East / right on screen), right side view, 4 walking frames, hand-drawn 45-degree oblique top-down game style, fixed camera angle across directions, character upright, no tilted camera, no dutch angle, consistent body proportions and outfit details, left-top lighting, solid flat background color #7A7A7A, no texture background, no text, no watermark, no border
```

- `SE（右下）`
```text
A 2D pseudo-3D character walk sprite sheet, post-apocalyptic survivor, walking down-right (South-East / down-right on screen), 3/4 front view, 4 walking frames, hand-drawn 45-degree oblique top-down game style, fixed camera angle across directions, character upright, no tilted camera, no dutch angle, consistent body proportions and outfit details, left-top lighting, solid flat background color #7A7A7A, no texture background, no text, no watermark, no border
```

- `S（向下）`
```text
A 2D pseudo-3D character walk sprite sheet, post-apocalyptic survivor, walking down (South / down on screen), front view (facing toward camera), 4 walking frames, hand-drawn 45-degree oblique top-down game style, fixed camera angle across directions, character upright, no tilted camera, no dutch angle, consistent body proportions and outfit details, left-top lighting, solid flat background color #7A7A7A, no texture background, no text, no watermark, no border
```

- `SW（左下）`
```text
A 2D pseudo-3D character walk sprite sheet, post-apocalyptic survivor, walking down-left (South-West / down-left on screen), 3/4 front view, 4 walking frames, hand-drawn 45-degree oblique top-down game style, fixed camera angle across directions, character upright, no tilted camera, no dutch angle, consistent body proportions and outfit details, left-top lighting, solid flat background color #7A7A7A, no texture background, no text, no watermark, no border
```

- `W（向左）`
```text
A 2D pseudo-3D character walk sprite sheet, post-apocalyptic survivor, walking left (West / left on screen), left side view, 4 walking frames, hand-drawn 45-degree oblique top-down game style, fixed camera angle across directions, character upright, no tilted camera, no dutch angle, consistent body proportions and outfit details, left-top lighting, solid flat background color #7A7A7A, no texture background, no text, no watermark, no border
```

- `NW（左上）`
```text
A 2D pseudo-3D character walk sprite sheet, post-apocalyptic survivor, walking up-left (North-West / up-left on screen), 3/4 back view, 4 walking frames, hand-drawn 45-degree oblique top-down game style, fixed camera angle across directions, character upright, no tilted camera, no dutch angle, consistent body proportions and outfit details, left-top lighting, solid flat background color #7A7A7A, no texture background, no text, no watermark, no border
```

---

## 8. 武器提示词（图标与地面拾取）

### 8.1 武器图标模板
```text
A 2D pseudo-3D weapon icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, worn metal and polymer parts, scratches and grime, clear gameplay readability, centered composition, transparent background, no text, no watermark, no border, 512x512.
Weapon: {武器描述}
```

### 8.2 武器示例（可直接用）
- `wpn_pistol_9mm`
```text
A 2D pseudo-3D weapon icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, compact 9mm pistol with worn slide and taped grip, subtle rust spots, muted palette, transparent background, no text, no watermark, no border, 512x512
```

- `wpn_shotgun_pump`
```text
A 2D pseudo-3D weapon icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, pump-action shotgun with scratched barrel and rough wooden fore-end, dusty and worn, transparent background, no text, no watermark, no border, 512x512
```

- `wpn_rifle_ak`
```text
A 2D pseudo-3D weapon icon in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, old assault rifle with worn receiver, tape-wrapped stock, surface rust and dirt, clear silhouette, transparent background, no text, no watermark, no border, 512x512
```

---

## 9. NPC 提示词（友方/中立）

- 友方幸存者
```text
A full-body 2D pseudo-3D friendly survivor NPC in hand-drawn 45-degree oblique top-down view, realistic old-industrial post-apocalyptic style, practical layered clothing, utility belt, worn fabric and metal details, calm posture, muted palette, transparent background, no text, no watermark, no border, 1024x1024
```

- 商人 NPC
```text
A full-body 2D pseudo-3D trader NPC in hand-drawn 45-degree oblique top-down view, post-apocalyptic old-industrial style, heavy coat with many pouches, portable crate straps, patched gloves, slightly suspicious expression, muted low-saturation colors, transparent background, no text, no watermark, no border, 1024x1024
```

- 技师 NPC
```text
A full-body 2D pseudo-3D mechanic NPC in hand-drawn 45-degree oblique top-down view, post-apocalyptic industrial style, welding goggles, tool belt, oil-stained clothes, compact posture, consistent left-top light, transparent background, no text, no watermark, no border, 1024x1024
```

---

## 10. 丧尸提示词（多种）

### 10.1 丧尸通用模板
```text
A full-body 2D pseudo-3D zombie enemy in hand-drawn 45-degree oblique top-down game style, realistic post-apocalyptic horror tone, torn clothes, decayed skin, asymmetrical silhouette, readable gameplay shape, muted dirty palette, consistent left-top light and soft shadow, transparent background, no text, no watermark, no border, 1024x1024
```

### 10.2 丧尸类型
- `zmb_walker`
```text
A full-body 2D pseudo-3D zombie walker in hand-drawn 45-degree oblique top-down style, thin decayed body, torn civilian clothes, dragging posture, readable silhouette, transparent background, no text, no watermark, no border, 1024x1024
```

- `zmb_runner`
```text
A full-body 2D pseudo-3D zombie runner in hand-drawn 45-degree oblique top-down style, lean muscular limbs, aggressive forward-lean pose, ripped sportswear remains, high-motion silhouette, transparent background, no text, no watermark, no border, 1024x1024
```

- `zmb_brute`
```text
A full-body 2D pseudo-3D brute zombie in hand-drawn 45-degree oblique top-down style, bulky body, heavy industrial protective remnants, swollen asymmetrical torso, intimidating silhouette, transparent background, no text, no watermark, no border, 1024x1024
```

- `zmb_spitter`
```text
A full-body 2D pseudo-3D spitter zombie in hand-drawn 45-degree oblique top-down style, distended chest and throat, corroded fluid stains around mouth, hunched posture, clear ranged-threat silhouette, transparent background, no text, no watermark, no border, 1024x1024
```

- `zmb_stalker`
```text
A full-body 2D pseudo-3D stalker zombie in hand-drawn 45-degree oblique top-down style, low crouch posture, elongated limbs, stealthy silhouette, ragged dark clothing, transparent background, no text, no watermark, no border, 1024x1024
```

### 10.3 丧尸动画序列模板
```text
A 2D pseudo-3D zombie sprite sheet in hand-drawn 45-degree oblique top-down style, 8 directions, each direction with 4 walk frames + 4 attack frames + 2 hit frames + 4 death frames, consistent model proportions and details, transparent background, no text, no watermark, no border.
Type: {walker/runner/brute/spitter/stalker}
```

---

## 11. Unity 命名建议
- 物品图标：`item_<id>_icon.png`
- 角色：`chr_player_<variant>_v1.png`
- NPC：`npc_<role>_v1.png`
- 丧尸：`zmb_<type>_v1.png`
- 武器：`wpn_<name>_icon.png`
- UI：`ui_inv_panel.png`、`ui_cell_empty.png`、`ui_cell_occupied.png`、`ui_cell_valid.png`、`ui_cell_invalid.png`

---

## 12. 批量生成建议（减少返工）
- 固定一张风格锚图后再批量生成。
- 每次只改对象描述，不改风格主句。
- 每个资产至少出 3 张候选，优先选轮廓最清晰者。
- 先静态图确认风格，再做序列帧动画。
- 出现风格漂移时，回到“通用主提示词 + 负面词”重开批次。
