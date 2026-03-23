#!/usr/bin/env python3
"""Generate zh.txt entries for Avalonia UI strings with Chinese translations."""
import os

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

with open(os.path.join(REPO_ROOT, 'new_translation_strings.txt'), 'r', encoding='utf-8') as f:
    strings = [s.strip() for s in f.readlines() if s.strip()]

# Chinese translations for common UI strings
zh_translations = {
    # Common buttons
    "Write": "写入",
    "Close": "关闭",
    "Cancel": "取消",
    "Apply": "应用",
    "OK": "确定",
    "Browse...": "浏览...",
    "Jump": "跳转",
    "Import PNG": "导入PNG",
    "Export PNG": "导出PNG",
    "Export PAL": "导出调色板",
    "Import PAL": "导入调色板",
    "Disassemble": "反汇编",
    "Find": "查找",
    "Save": "保存",
    "Export": "导出",
    "Import": "导入",
    "Refresh": "刷新",
    "Reset": "重置",
    "Delete": "删除",
    "Add": "添加",
    "Remove": "移除",
    "Copy": "复制",
    "Paste": "粘贴",
    "Select All": "全选",
    "Export CSV": "导出CSV",
    "Import CSV": "导入CSV",
    "Export TSV": "导出TSV",
    "Import TSV": "导入TSV",
    "Export MIDI": "导出MIDI",
    "Import MIDI": "导入MIDI",
    "Play": "播放",
    "Stop": "停止",
    "Undo": "撤销",

    # Common labels
    "Address:": "地址:",
    "Name:": "名称:",
    "Description:": "描述:",
    "Preview:": "预览:",
    "Value:": "值:",
    "Unit:": "单位:",
    "Unit ID:": "单位ID:",
    "Class:": "职业:",
    "Class ID:": "职业ID:",
    "Item 1:": "物品1:",
    "Item 2:": "物品2:",
    "Item 3:": "物品3:",
    "Item 4:": "物品4:",
    "Level:": "等级:",
    "HP:": "HP:",
    "Str:": "力量:",
    "Skl:": "技术:",
    "Spd:": "速度:",
    "Def:": "防御:",
    "Res:": "魔防:",
    "Lck:": "幸运:",
    "Con:": "体格:",
    "Mov:": "移动:",
    "Pow:": "力量:",
    "HP": "HP",
    "Str/Mag": "力/魔",
    "Skill": "技术",
    "Speed": "速度",
    "Luck": "幸运",
    "Defense": "防御",
    "Resistance": "魔防",
    "Text ID:": "文本ID:",
    "Description Text ID:": "描述文本ID:",
    "Palette:": "调色板:",
    "Palette Pointer:": "调色板指针:",
    "Image Pointer:": "图像指针:",
    "TSA Pointer:": "TSA指针:",
    "Anime Pointer:": "动画指针:",
    "Animation Pointer:": "动画指针:",
    "Pointer:": "指针:",
    "Offset:": "偏移:",
    "Width:": "宽度:",
    "Height:": "高度:",
    "Size:": "大小:",
    "Count:": "数量:",
    "Type:": "类型:",
    "X Position:": "X坐标:",
    "Y Position:": "Y坐标:",
    "X:": "X:",
    "Y:": "Y:",
    "Zoom:": "缩放:",

    # Section headers
    "Identity": "身份",
    "Base Stats": "基础属性",
    "Growth Rates": "成长率",
    "Weapon Levels": "武器等级",
    "Personal Skills": "个人技能",
    "Class Skills": "职业技能",
    "Ability Flags": "能力标志",
    "Ability Flags:": "能力标志:",
    "Movement Cost": "移动消耗",
    "Promotion": "转职",
    "Promotions": "转职",
    "Terrain Effects": "地形效果",
    "Map Settings": "地图设置",
    "Battle Animation": "战斗动画",
    "Animation Data Details": "动画数据详情",
    "Animation Frame Viewer": "动画帧查看器",
    "Palette Editor": "调色板编辑器",
    "General": "通用",
    "Advanced": "高级",
    "Settings": "设置",
    "Options": "选项",
    "Actions": "操作",
    "Details": "详情",
    "Properties": "属性",
    "Commands": "命令",

    # Editor types
    "Unit Editor": "单位编辑器",
    "Class Editor": "职业编辑器",
    "Item Editor": "物品编辑器",
    "Map Editor": "地图编辑器",
    "Text Viewer": "文本查看器",
    "Hex Editor": "十六进制编辑器",
    "Portrait Editor": "肖像编辑器",
    "Song Table": "歌曲表",
    "Patch Manager": "补丁管理器",
    "Font Editor": "字体编辑器",
    "Event Script": "事件脚本",
    "Data Export": "数据导出",
    "Pointer Tool": "指针工具",
    "Error Report": "错误报告",

    # AI labels
    "AI 1:": "AI 1:",
    "AI 2:": "AI 2:",
    "AI ASM Call": "AI ASM调用",
    "AI ASM Call Talk": "AI ASM调用对话",
    "AI Coordinate Editor": "AI坐标编辑器",
    "AI Item Performance": "AI物品执行",
    "AI Map Settings": "AI地图设置",
    "AI Range Editor": "AI范围编辑器",
    "AI Script Editor": "AI脚本编辑器",
    "AI Staff Performance": "AI杖执行",
    "AI Steal Item Logic": "AI偷取物品逻辑",
    "AI Targeting": "AI目标选定",
    "AI Tiles Evaluation": "AI地格评估",
    "AI Units Evaluation": "AI单位评估",
    "AI Scripts": "AI脚本",

    # Weapon types
    "Sword:": "剑:",
    "Lance:": "枪:",
    "Axe:": "斧:",
    "Bow:": "弓:",
    "Staff:": "杖:",
    "Anima:": "理:",
    "Light:": "光:",
    "Dark:": "暗:",

    # Event-related
    "Event Conditions": "事件条件",
    "Event Unit": "事件单位",
    "Event Haiku": "事件俳句",
    "Event Battle Talk": "事件战斗对话",
    "Force Sortie": "强制出击",
    "Map Change": "地图变更",

    # Map-related
    "Map Pointer": "地图指针",
    "Map ID:": "地图ID:",
    "Chapter ID:": "章节ID:",
    "Map Data:": "地图数据:",
    "Tile Animation": "地块动画",
    "Terrain Names": "地形名称",
    "Move Cost": "移动消耗",
    "Exit Points": "出口点",

    # Graphics
    "Portrait": "肖像",
    "Battle Terrain": "战斗地形",
    "Battle Background": "战斗背景",
    "Chapter Title": "章节标题",
    "CG Viewer": "CG查看器",
    "OAM Sprite Viewer": "OAM精灵查看器",

    # Audio
    "Song ID:": "歌曲ID:",
    "Song Name:": "歌曲名称:",
    "Song Table Editor": "歌曲表编辑器",
    "Song Track Editor": "歌曲轨道编辑器",
    "Instrument:": "乐器:",
    "Volume:": "音量:",
    "Tempo:": "节拍:",
    "Sound Room": "音乐室",
    "Boss BGM": "Boss BGM",
    "Footsteps": "脚步声",
    "Song Number:": "歌曲编号:",

    # Support
    "Support Units": "支援单位",
    "Support Talk": "支援对话",
    "Support Attribute": "支援属性",
    "Affinity:": "相性:",
    "Affinity Type:": "相性类型:",

    # World Map
    "World Map Points": "世界地图节点",
    "World Map Paths": "世界地图路径",
    "World Map BGM": "世界地图BGM",
    "World Map Events": "世界地图事件",

    # Arena
    "Arena Class": "竞技场职业",
    "Arena Enemy Weapon": "竞技场敌方武器",
    "Link Arena Deny": "通信竞技场禁止",

    # Monsters
    "Monster Probability": "魔物概率",
    "Monster Items": "魔物物品",

    # Summons
    "Summon Unit": "召唤单位",
    "Demon King": "魔王",

    # CC Branch
    "CC Branch": "转职分支",
    "Advanced Class 1:": "上级职业1:",
    "Advanced Class 2:": "上级职业2:",
    "Advanced Class 3:": "上级职业3:",
    "Advanced Class 4:": "上级职业4:",

    # Menu
    "Menu Definition": "菜单定义",
    "Menu Command": "菜单命令",

    # Credits/Ending
    "Ending Events": "结局事件",
    "Staff Roll": "制作人员名单",
    "Senseki Comment": "战绩评语",

    # Misc UI
    "Status:": "状态:",
    "Installed": "已安装",
    "Not Installed": "未安装",
    "Available": "可用",
    "Enabled": "已启用",
    "Disabled": "已禁用",
    "Unknown": "未知",
    "Unknown 1:": "未知1:",
    "Unknown 2:": "未知2:",
    "Entry Size:": "条目大小:",
    "Entry Count:": "条目数量:",
    "Magic Effect:": "魔法效果:",
    "Map Face:": "地图头像:",
    "Battle Anime:": "战斗动画:",
    "Ability 1:": "能力1:",
    "Ability 2:": "能力2:",
    "Ability 3:": "能力3:",
    "Ability 4:": "能力4:",

    # Data export/import
    "Table:": "表:",
    "Format:": "格式:",
    "Output:": "输出:",
    "Data Export/Import": "数据导出/导入",
    "Export Data": "导出数据",
    "Import Data": "导入数据",

    # Hex editor
    "Length:": "长度:",
    "Go": "前往",
    "Search": "搜索",
    "Replace": "替换",

    # Disassembly
    "Disassembly Output": "反汇编输出",
    "Start Address:": "起始地址:",
    "End Address:": "结束地址:",

    # Options
    "Language:": "语言:",
    "Auto Backup": "自动备份",
    "Auto Save": "自动保存",
    "External Tools": "外部工具",

    # Patch Manager
    "Install Patch": "安装补丁",
    "Uninstall Patch": "卸载补丁",
    "Patch Details": "补丁详情",

    # Merge
    "Three-Way Merge": "三方合并",
    "Merge": "合并",

    # Easy mode
    "Easy Mode": "简易模式",
    "Edit Units": "编辑单位",
    "Edit Classes": "编辑职业",
    "Edit Items": "编辑物品",
    "Characters": "角色",
    "Items": "物品",
    "Maps": "地图",
    "Events": "事件",
    "Graphics": "图形",
    "Music": "音乐",
    "Text": "文本",
    "Tools": "工具",
    "Export Texts": "导出文本",
    "Import Texts": "导入文本",
    "Lint Check": "检查错误",

    # Context menu
    "Copy Address": "复制地址",
    "Copy Name": "复制名称",
    "Copy Hex Data": "复制十六进制数据",
    "Double-click or press Enter to select": "双击或按回车选择",

    # Misc views
    "Animation Creator": "动画创建器",
    "Growth Simulator": "成长模拟器",
    "--- Growth Simulator ---": "--- 成长模拟器 ---",
    "FE-Repo Resource Browser": "FE-Repo资源浏览器",
    "Category:": "分类:",
    "Preview Area": "预览区域",

    # Script editor
    "Compile": "编译",
    "Run": "运行",
    "Event Assembler": "事件汇编器",

    # Translate view
    "Dev Translate": "开发翻译",
    "Translate": "翻译",

    # Image editor
    "Grid Overlay": "网格覆盖",
    "Transparent": "透明",

    # Error dialogs
    "Error": "错误",
    "Warning": "警告",
    "Information": "信息",
    "Confirm": "确认",
}

lines = []
lines.append("")
lines.append("# ========================================================")
lines.append("# Avalonia UI strings \u2014 Chinese translations")
lines.append("# ========================================================")
lines.append("")

translated_count = 0
untranslated_count = 0

for s in strings:
    escaped = s.replace('\r\n', '\\r\\n')
    lines.append(f":{escaped}")
    if s in zh_translations:
        lines.append(zh_translations[s])
        translated_count += 1
    else:
        # Use English as placeholder
        lines.append(escaped)
        untranslated_count += 1
    lines.append("")

outpath = os.path.join(REPO_ROOT, 'avalonia_zh_entries.txt')
with open(outpath, 'w', encoding='utf-8') as f:
    f.write('\n'.join(lines))

print(f"Generated {len(strings)} entries for zh.txt")
print(f"  Translated: {translated_count}")
print(f"  Placeholder (English): {untranslated_count}")
