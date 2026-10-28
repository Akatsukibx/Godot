using Godot;
using System.Collections.Generic;

// Skill and item icons as inline SVG, rasterised at runtime (no import step needed).
public static class Icons
{
    static readonly Dictionary<string, Texture2D> Cache = new();

    static string Svg(string body) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 64 64'>{body}</svg>";

    static readonly Dictionary<string, string> Sources = new()
    {
        ["fire_talisman"] = Svg(
            "<rect x='17' y='5' width='30' height='54' rx='3' fill='#f3d27a' stroke='#8a5a1a' stroke-width='2'/>" +
            "<path d='M23 13h18M23 18h18' stroke='#a3322a' stroke-width='2'/>" +
            "<path d='M32 52c-8 0-11-7-8-13 2-4 6-6 5-13 6 4 9 9 8 14 2-1 3-3 4-6 3 5 3 18-9 18z' fill='#e2462b'/>" +
            "<path d='M32 52c-3 0-5-3-3-6 1-2 3-3 3-6 3 3 5 12 0 12z' fill='#ffcf4a'/>"),
        ["rice"] = Svg(
            "<path d='M8 36h48c0 13-11 21-24 21S8 49 8 36z' fill='#7a4a2a'/>" +
            "<ellipse cx='32' cy='36' rx='24' ry='5' fill='#f5f0e0'/>" +
            "<g fill='#fffbe8'><ellipse cx='20' cy='22' rx='2' ry='4' transform='rotate(-30 20 22)'/>" +
            "<ellipse cx='32' cy='14' rx='2' ry='4' transform='rotate(10 32 14)'/>" +
            "<ellipse cx='44' cy='20' rx='2' ry='4' transform='rotate(40 44 20)'/>" +
            "<ellipse cx='27' cy='27' rx='2' ry='4' transform='rotate(-60 27 27)'/>" +
            "<ellipse cx='39' cy='28' rx='2' ry='4' transform='rotate(60 39 28)'/>" +
            "<ellipse cx='12' cy='12' rx='2' ry='4' transform='rotate(-20 12 12)'/>" +
            "<ellipse cx='52' cy='10' rx='2' ry='4' transform='rotate(30 52 10)'/></g>"),
        ["holy_water"] = Svg(
            "<path d='M32 6C25 19 15 28 15 40a17 17 0 0 0 34 0C49 28 39 19 32 6z' fill='#4fa8f5' stroke='#1d5b9c' stroke-width='2'/>" +
            "<path d='M24 38a8 8 0 0 0 6 10' stroke='#d6efff' stroke-width='3' fill='none' stroke-linecap='round'/>" +
            "<path d='M48 8l2 5 5 2-5 2-2 5-2-5-5-2 5-2z' fill='#fff6b0'/>"),
        ["sword"] = Svg(
            "<path d='M50 6l8 0 0 8-30 30-8-8z' fill='#d8dee8' stroke='#6b7380' stroke-width='2'/>" +
            "<path d='M14 38l12 12' stroke='#c9a03a' stroke-width='6' stroke-linecap='round'/>" +
            "<path d='M20 44l-12 12' stroke='#5a3a22' stroke-width='6' stroke-linecap='round'/>" +
            "<circle cx='7' cy='57' r='4' fill='#c9a03a'/>"),
        ["spin"] = Svg(
            "<circle cx='32' cy='32' r='22' fill='none' stroke='#f0c060' stroke-width='5' stroke-dasharray='30 8'/>" +
            "<path d='M50 14l6 2-2 7' fill='none' stroke='#f0c060' stroke-width='4' stroke-linecap='round'/>" +
            "<path d='M14 50l-6-2 2-7' fill='none' stroke='#f0c060' stroke-width='4' stroke-linecap='round'/>" +
            "<path d='M40 20l4 0 0 4-16 16-4-4z' fill='#d8dee8'/>"),
        ["shield"] = Svg(
            "<path d='M32 5l23 8v16c0 15-10 25-23 30C19 54 9 44 9 29V13z' fill='#d9a632' stroke='#7a5a12' stroke-width='2'/>" +
            "<path d='M32 15v34M20 24h24M22 34h20' stroke='#7a2a12' stroke-width='2.5'/>" +
            "<circle cx='32' cy='24' r='4' fill='none' stroke='#7a2a12' stroke-width='2'/>"),
        ["dash"] = Svg(
            "<path d='M8 20h22M4 32h26M8 44h22' stroke='#9fd4ff' stroke-width='4' stroke-linecap='round'/>" +
            "<path d='M34 12l20 20-20 20' fill='none' stroke='#e8f4ff' stroke-width='7' stroke-linejoin='round' stroke-linecap='round'/>"),
        ["ya_hom"] = Svg(
            "<rect x='26' y='6' width='12' height='8' rx='2' fill='#7a4a2a'/>" +
            "<path d='M24 14h16v6c6 3 9 8 9 15v15a6 6 0 0 1-6 6H21a6 6 0 0 1-6-6V35c0-7 3-12 9-15z' fill='#5dbb63' stroke='#2a6b2f' stroke-width='2'/>" +
            "<rect x='21' y='34' width='22' height='14' rx='2' fill='#f5ecd0'/>" +
            "<path d='M27 41h10M32 36v10' stroke='#c0392b' stroke-width='3'/>"),
        ["straw"] = Svg(
            "<g stroke='#d8b45a' stroke-width='3' stroke-linecap='round'>" +
            "<path d='M22 56L30 8M28 56L32 6M34 56L34 6M40 56L36 8M46 56L38 10M16 54L28 12'/></g>" +
            "<rect x='18' y='30' width='28' height='6' rx='2' fill='#8a5a2a'/>"),
        ["lantern"] = Svg(
            "<path d='M24 8h16M32 4v6' stroke='#444' stroke-width='3'/>" +
            "<rect x='18' y='12' width='28' height='40' rx='6' fill='#1f3a2a' stroke='#2a2a2a' stroke-width='2'/>" +
            "<ellipse cx='32' cy='32' rx='10' ry='14' fill='#7dff9a'/>" +
            "<ellipse cx='32' cy='32' rx='5' ry='8' fill='#e6ffe8'/>" +
            "<rect x='16' y='50' width='32' height='6' rx='2' fill='#2a2a2a'/>"),
        ["thread"] = Svg(
            "<path d='M10 40c8-20 18 10 26-10s16 6 18-8' fill='none' stroke='#f2f2f2' stroke-width='4' stroke-linecap='round'/>" +
            "<path d='M10 48c8-14 18 6 26-6' fill='none' stroke='#dcdcdc' stroke-width='3' stroke-linecap='round'/>"),
        ["spirit"] = Svg(
            "<defs><radialGradient id='g'><stop offset='0' stop-color='#ffffff'/><stop offset='0.45' stop-color='#bfe0ff'/>" +
            "<stop offset='1' stop-color='#5aa0ff' stop-opacity='0'/></radialGradient></defs>" +
            "<circle cx='32' cy='32' r='28' fill='url(#g)'/>"),
        ["sun"] = Svg(
            "<circle cx='32' cy='32' r='12' fill='#ffd45a'/>" +
            "<g stroke='#ffd45a' stroke-width='4' stroke-linecap='round'>" +
            "<path d='M32 6v8M32 50v8M6 32h8M50 32h8M13 13l6 6M45 45l6 6M13 51l6-6M45 19l6-6'/></g>"),
        ["moon"] = Svg("<path d='M40 8a24 24 0 1 0 16 38A20 20 0 0 1 40 8z' fill='#cfd8ff'/>"),
        ["triple_talisman"] = Svg(
            "<g transform='rotate(-25 32 40)'><rect x='26' y='10' width='14' height='30' rx='2' fill='#f3d27a' stroke='#8a5a1a' stroke-width='1.5'/></g>" +
            "<g transform='rotate(25 32 40)'><rect x='24' y='10' width='14' height='30' rx='2' fill='#f3d27a' stroke='#8a5a1a' stroke-width='1.5'/></g>" +
            "<rect x='25' y='6' width='14' height='32' rx='2' fill='#ffe08a' stroke='#8a5a1a' stroke-width='1.5'/>" +
            "<path d='M29 14h6M29 19h6' stroke='#a3322a' stroke-width='2'/>" +
            "<path d='M32 60c-7 0-9-6-6-10 2-3 4-4 4-8 5 3 7 6 6 10 1-1 2-2 3-4 2 4 2 12-7 12z' fill='#e2462b'/>"),
        ["thrust"] = Svg(
            "<path d='M6 32h10M4 24h8M4 40h8' stroke='#9fd4ff' stroke-width='3' stroke-linecap='round'/>" +
            "<path d='M20 29h30l8 3-8 3H20z' fill='#e4e9f0' stroke='#6b7380' stroke-width='1.5'/>" +
            "<rect x='16' y='24' width='5' height='16' rx='1' fill='#c9a03a'/><rect x='9' y='30' width='8' height='4' fill='#5a3a22'/>"),
        ["ya_hom_yai"] = Svg(
            "<rect x='24' y='4' width='16' height='8' rx='2' fill='#7a4a2a'/>" +
            "<path d='M22 12h20v6c7 3 10 9 10 17v17a6 6 0 0 1-6 6H18a6 6 0 0 1-6-6V35c0-8 3-14 10-17z' fill='#3fa0d8' stroke='#1d5b8c' stroke-width='2'/>" +
            "<rect x='19' y='32' width='26' height='16' rx='2' fill='#f5ecd0'/>" +
            "<path d='M26 40h12M32 34v12' stroke='#c0392b' stroke-width='3'/>" +
            "<circle cx='47' cy='20' r='5' fill='#ffffff' opacity='0.8'/>"),
        ["sword_iron"] = Svg(
            "<path d='M50 6l8 0 0 8-30 30-8-8z' fill='#9aa3ad' stroke='#4b5360' stroke-width='2'/>" +
            "<path d='M50 10L26 34' stroke='#c9d0d8' stroke-width='2'/>" +
            "<path d='M14 38l12 12' stroke='#7a7a7a' stroke-width='6' stroke-linecap='round'/>" +
            "<path d='M20 44l-12 12' stroke='#3a2a1a' stroke-width='6' stroke-linecap='round'/>"),
        ["sword_holy"] = Svg(
            "<circle cx='38' cy='26' r='24' fill='#ffe9a0' opacity='0.35'/>" +
            "<path d='M50 6l8 0 0 8-30 30-8-8z' fill='#fff4d0' stroke='#c9a03a' stroke-width='2'/>" +
            "<path d='M14 38l12 12' stroke='#e8b830' stroke-width='6' stroke-linecap='round'/>" +
            "<path d='M20 44l-12 12' stroke='#7a2a12' stroke-width='6' stroke-linecap='round'/>" +
            "<circle cx='7' cy='57' r='4' fill='#e8b830'/>"),
        ["talisman_green"] = Svg(
            "<rect x='17' y='5' width='30' height='54' rx='3' fill='#d8f0c8' stroke='#2a6b3a' stroke-width='2'/>" +
            "<path d='M23 13h18M23 18h18' stroke='#1f6b2a' stroke-width='2'/>" +
            "<ellipse cx='32' cy='40' rx='9' ry='12' fill='#7dff9a'/><ellipse cx='32' cy='40' rx='4' ry='6' fill='#ecffee'/>"),
        ["talisman_gold"] = Svg(
            "<rect x='15' y='4' width='34' height='56' rx='3' fill='#f7d34a' stroke='#8a5a1a' stroke-width='2.5'/>" +
            "<rect x='20' y='9' width='24' height='46' rx='2' fill='none' stroke='#a3322a' stroke-width='1.5'/>" +
            "<path d='M32 14v36M24 22h16M24 32h16M26 42h12' stroke='#a3322a' stroke-width='2'/>" +
            "<circle cx='32' cy='27' r='5' fill='none' stroke='#a3322a' stroke-width='2'/>"),
        ["takrut"] = Svg(
            "<path d='M10 14c10 6 34 6 44 0' fill='none' stroke='#f2f2f2' stroke-width='2.5'/>" +
            "<rect x='20' y='22' width='24' height='30' rx='10' fill='#c99a3a' stroke='#6a4a12' stroke-width='2'/>" +
            "<path d='M20 30h24M20 37h24M20 44h24' stroke='#f2f2f2' stroke-width='2.5'/>" +
            "<path d='M32 14v8' stroke='#f2f2f2' stroke-width='2.5'/>"),
        ["quest"] = Svg(
            "<rect x='14' y='10' width='36' height='46' rx='3' fill='#efe0b8' stroke='#7a5a2a' stroke-width='2'/>" +
            "<rect x='10' y='6' width='44' height='8' rx='4' fill='#b8925a'/><rect x='10' y='52' width='44' height='8' rx='4' fill='#b8925a'/>" +
            "<path d='M21 22h22M21 30h22M21 38h14' stroke='#6a4a2a' stroke-width='2.5'/>" +
            "<path d='M40 36l4 4 8-10' stroke='#c0392b' stroke-width='3.5' fill='none' stroke-linecap='round'/>"),
        ["bag"] = Svg(
            "<path d='M22 18c0-8 20-8 20 0' fill='none' stroke='#7a4a2a' stroke-width='4'/>" +
            "<path d='M14 24h36l4 30a4 4 0 0 1-4 4H14a4 4 0 0 1-4-4z' fill='#b06a3a' stroke='#5a3018' stroke-width='2'/>" +
            "<path d='M12 32h40' stroke='#7a3e1a' stroke-width='3'/><rect x='27' y='28' width='10' height='9' rx='2' fill='#e8c060'/>"),
        ["status"] = Svg(
            "<circle cx='32' cy='18' r='10' fill='#e9e2c8' stroke='#6a5a3a' stroke-width='2'/>" +
            "<path d='M12 56c0-14 9-22 20-22s20 8 20 22z' fill='#9a3a2a' stroke='#5a1a12' stroke-width='2'/>" +
            "<path d='M44 8l2 5 5 2-5 2-2 5-2-5-5-2 5-2z' fill='#ffd45a'/>"),
        ["map"] = Svg(
            "<path d='M6 14l16-6 20 6 16-6v42l-16 6-20-6-16 6z' fill='#d8c79a' stroke='#6a5a3a' stroke-width='2'/>" +
            "<path d='M22 8v42M42 14v42' stroke='#6a5a3a' stroke-width='1.5'/>" +
            "<path d='M12 40c6-4 10 2 16-4s10-2 14-8' stroke='#3a7ab8' stroke-width='3' fill='none'/>" +
            "<path d='M44 22l6 6M50 22l-6 6' stroke='#c0392b' stroke-width='3'/>"),
        ["craft"] = Svg(
            "<path d='M12 40h40l-6 16H18z' fill='#8a6a4a' stroke='#4a3420' stroke-width='2'/>" +
            "<ellipse cx='32' cy='40' rx='20' ry='5' fill='#5a4430'/>" +
            "<path d='M40 8l8 4-14 26-5-2z' fill='#c9b08a' stroke='#6a5038' stroke-width='2'/>" +
            "<circle cx='24' cy='38' r='2' fill='#7dff9a'/><circle cx='34' cy='39' r='2' fill='#f0d060'/>"),
        // ---------- materials ----------
        ["wood"] = Svg("<rect x='10' y='22' width='44' height='12' rx='5' fill='#7a5a3a' stroke='#3a2a18' stroke-width='2' transform='rotate(-15 32 28)'/>" +
            "<rect x='12' y='34' width='40' height='11' rx='5' fill='#6a4a2e' stroke='#3a2a18' stroke-width='2' transform='rotate(10 32 40)'/>" +
            "<circle cx='14' cy='32' r='3' fill='#c9a070'/>"),
        ["iron"] = Svg("<path d='M10 40l10-18h26l8 18z' fill='#7a6a60' stroke='#3a302a' stroke-width='2'/>" +
            "<path d='M18 34h28' stroke='#a8644a' stroke-width='3'/><circle cx='24' cy='29' r='2' fill='#b0703a'/><circle cx='40' cy='37' r='2.5' fill='#b0703a'/>"),
        ["iron_blue"] = Svg("<path d='M10 40l10-18h26l8 18z' fill='#6a8ab0' stroke='#24344a' stroke-width='2'/>" +
            "<path d='M18 30h28' stroke='#cfe2ff' stroke-width='2'/><path d='M48 12l2 5 5 2-5 2-2 5-2-5-5-2 5-2z' fill='#fff6b0'/>"),
        ["cloth"] = Svg("<path d='M12 14h40v36c-8-4-12 4-20 0s-12 4-20 0z' fill='#e8dcb8' stroke='#8a7a50' stroke-width='2'/>" +
            "<path d='M24 22h16M24 28h16M28 34h8' stroke='#a3322a' stroke-width='2.5'/>"),
        ["wax"] = Svg("<path d='M18 50c0-14 6-22 14-30 8 8 14 16 14 30z' fill='#f0d080' stroke='#9a7a2a' stroke-width='2'/>" +
            "<ellipse cx='32' cy='50' rx='14' ry='4' fill='#d8b860'/><path d='M44 10l2 5 5 2-5 2-2 5-2-5-5-2 5-2z' fill='#fff6b0'/>"),
        ["dew"] = Svg("<path d='M32 8C26 22 18 30 18 40a14 14 0 0 0 28 0C46 30 38 22 32 8z' fill='#9ac8ff' stroke='#3a5aa0' stroke-width='2'/>" +
            "<path d='M40 18l3-6' stroke='#cfd8ff' stroke-width='2'/><circle cx='26' cy='40' r='4' fill='#e8f2ff'/><path d='M50 30l1.5 4 4 1.5-4 1.5-1.5 4-1.5-4-4-1.5 4-1.5z' fill='#d8c8ff'/>"),
        ["crystal"] = Svg("<path d='M32 6l14 18-14 34-14-34z' fill='#b8a0ff' stroke='#5a3aa0' stroke-width='2'/>" +
            "<path d='M18 24h28M32 6v52' stroke='#e8deff' stroke-width='1.5'/>"),
        ["seal"] = Svg("<circle cx='32' cy='32' r='22' fill='#f0b040' stroke='#8a5a10' stroke-width='3'/>" +
            "<path d='M32 14l5 12 13 1-10 8 4 13-12-7-12 7 4-13-10-8 13-1z' fill='#fff0b0'/><path d='M44 44l10 10' stroke='#3a2a10' stroke-width='3'/>"),
        ["smoke"] = Svg("<circle cx='24' cy='38' r='12' fill='#8a8aa0'/><circle cx='38' cy='32' r='14' fill='#9a9ab0'/><circle cx='30' cy='22' r='10' fill='#b0b0c4'/>" +
            "<path d='M20 52h24' stroke='#6aa060' stroke-width='3' stroke-linecap='round'/>"),
        // ---------- weapons and gear ----------
        ["dagger"] = Svg("<path d='M44 8l6 2-22 30-4-4z' fill='#d8dee8' stroke='#6b7380' stroke-width='2'/>" +
            "<path d='M16 36l12 12' stroke='#4a4a52' stroke-width='5' stroke-linecap='round'/><path d='M22 42l-10 10' stroke='#3a2a1a' stroke-width='5' stroke-linecap='round'/>"),
        ["dagger_moon"] = Svg("<circle cx='42' cy='20' r='12' fill='#cfd8ff' opacity='0.35'/>" +
            "<path d='M44 8l6 2-22 30-4-4z' fill='#e8eeff' stroke='#5a6aa0' stroke-width='2'/>" +
            "<path d='M16 36l12 12' stroke='#3a3a6a' stroke-width='5' stroke-linecap='round'/><path d='M22 42l-10 10' stroke='#1a1a2a' stroke-width='5' stroke-linecap='round'/>"),
        ["shield_wood"] = Svg("<circle cx='32' cy='32' r='24' fill='#8a6a42' stroke='#4a3418' stroke-width='3'/>" +
            "<path d='M32 8v48M8 32h48' stroke='#6a4e2c' stroke-width='2'/><rect x='26' y='24' width='12' height='16' fill='#f0d080'/><path d='M29 28h6M29 33h6' stroke='#a3322a' stroke-width='2'/>"),
        ["helmet_cloth"] = Svg("<path d='M12 40c0-16 9-26 20-26s20 10 20 26z' fill='#e8dcb8' stroke='#8a7a50' stroke-width='2'/>" +
            "<path d='M12 40h40' stroke='#a3322a' stroke-width='4'/><rect x='28' y='22' width='8' height='10' fill='#f0c060'/>"),
        ["armor_cloth"] = Svg("<path d='M20 10l12 6 12-6 10 10-6 8v26H16V28l-6-8z' fill='#e8dcb8' stroke='#8a7a50' stroke-width='2'/>" +
            "<path d='M26 28h12M26 34h12M28 40h8' stroke='#a3322a' stroke-width='2'/>"),
        ["pants_cloth"] = Svg("<path d='M18 10h28l4 44h-12l-6-28-6 28H14z' fill='#c8b890' stroke='#7a6a40' stroke-width='2'/><path d='M18 16h28' stroke='#a3322a' stroke-width='3'/>"),
        ["boots_cloth"] = Svg("<path d='M14 14h14v28h18a6 6 0 0 1 6 6v6H14z' fill='#8a7a5a' stroke='#4a3a24' stroke-width='2'/><path d='M14 22h14' stroke='#e8dcb8' stroke-width='3'/>"),
        ["helmet_thread"] = Svg("<path d='M12 42c0-17 9-28 20-28s20 11 20 28z' fill='#cfd2d8' stroke='#5a5e68' stroke-width='2'/>" +
            "<path d='M16 30h32M14 36h36M22 18l20 0' stroke='#f4f4f4' stroke-width='2'/><path d='M10 42h44' stroke='#5a5e68' stroke-width='4'/>"),
        ["armor_thread"] = Svg("<path d='M20 10l12 6 12-6 10 10-6 8v26H16V28l-6-8z' fill='#cfd2d8' stroke='#5a5e68' stroke-width='2'/>" +
            "<path d='M18 26h28M18 32h28M18 38h28M18 44h28' stroke='#f4f4f4' stroke-width='1.8'/>"),
        ["pants_thread"] = Svg("<path d='M18 10h28l4 44h-12l-6-28-6 28H14z' fill='#a8acb4' stroke='#4a4e58' stroke-width='2'/><path d='M16 22h32M15 32h12M37 32h12' stroke='#f0f0f0' stroke-width='2'/>"),
        ["boots_thread"] = Svg("<path d='M14 14h14v28h18a6 6 0 0 1 6 6v6H14z' fill='#6a6e78' stroke='#2a2e38' stroke-width='2'/><path d='M14 22h14M14 30h14' stroke='#e8e8e8' stroke-width='2'/>"),
        ["necklace"] = Svg("<path d='M14 12c0 22 8 30 18 30s18-8 18-30' fill='none' stroke='#8a3a2a' stroke-width='2.5'/>" +
            "<g fill='#7a5a3a'><circle cx='16' cy='24' r='3'/><circle cx='20' cy='33' r='3'/><circle cx='26' cy='39' r='3'/><circle cx='38' cy='39' r='3'/><circle cx='44' cy='33' r='3'/><circle cx='48' cy='24' r='3'/></g>" +
            "<circle cx='32' cy='48' r='6' fill='#c99a3a' stroke='#6a4a12' stroke-width='2'/>"),
        ["ring"] = Svg("<circle cx='32' cy='38' r='16' fill='none' stroke='#c99a3a' stroke-width='6'/>" +
            "<path d='M24 22c4-6 12-6 16 0' fill='none' stroke='#f2f2f2' stroke-width='3'/><circle cx='32' cy='20' r='5' fill='#d84a3a' stroke='#7a1a12' stroke-width='2'/>"),
        ["rain"] = Svg(
            "<path d='M16 34a12 12 0 0 1 4-23 15 15 0 0 1 28 4 10 10 0 0 1 2 19z' fill='#9aa6bd'/>" +
            "<path d='M20 42l-4 10M32 42l-4 12M44 42l-4 10' stroke='#7fc0ff' stroke-width='3.5' stroke-linecap='round'/>"),
        ["stamina"] = Svg(
            "<path d='M36 4L14 36h14l-4 24 26-36H36z' fill='#f0c040' stroke='#8a6010' stroke-width='2' stroke-linejoin='round'/>"),
        // UI widgets (not square, so they get their own sizes)
        ["toggle_on"] = "<svg xmlns='http://www.w3.org/2000/svg' width='46' height='24' viewBox='0 0 46 24'>" +
            "<rect x='1' y='1' width='44' height='22' rx='2' fill='#5a5d62' stroke='#9a9da2' stroke-width='1'/>" +
            "<rect x='24' y='3' width='19' height='18' rx='1.5' fill='#e8e9ea'/></svg>",
        ["toggle_off"] = "<svg xmlns='http://www.w3.org/2000/svg' width='46' height='24' viewBox='0 0 46 24'>" +
            "<rect x='1' y='1' width='44' height='22' rx='2' fill='#1e1f22' stroke='#5a5d62' stroke-width='1'/>" +
            "<rect x='3' y='3' width='19' height='18' rx='1.5' fill='#7a7d82'/></svg>",
        ["grabber"] = "<svg xmlns='http://www.w3.org/2000/svg' width='14' height='22' viewBox='0 0 14 22'>" +
            "<rect x='1' y='1' width='12' height='20' rx='1.5' fill='#f2f3f4' stroke='#2a2b2e' stroke-width='1'/></svg>",
        ["unknown"] = Svg("<circle cx='32' cy='32' r='20' fill='#555'/><text x='32' y='40' font-size='24' text-anchor='middle' fill='#fff'>?</text>"),
    };

    public static Texture2D Get(string id)
    {
        id = string.IsNullOrEmpty(id) || !Sources.ContainsKey(id) ? "unknown" : id;
        if (Cache.TryGetValue(id, out var tex))
            return tex;
        var image = new Image();
        if (image.LoadSvgFromString(Sources[id], 1f) != Error.Ok)
            return null;
        tex = ImageTexture.CreateFromImage(image);
        Cache[id] = tex;
        return tex;
    }
}
