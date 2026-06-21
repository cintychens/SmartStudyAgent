# OCR 扫描版 PDF 识别说明

SmartStudyAgent 已经接入可选 OCR 流程。上传 PDF 时，系统会先尝试普通文本提取；如果提取不到文字，会自动尝试 OCR。

## 需要安装的工具

OCR 依赖本机外部工具：

- Tesseract OCR：识别图片文字。
- Poppler 的 `pdftoppm` 或 MuPDF 的 `mutool`：把 PDF 页面渲染成图片。

Windows 可以用：

```powershell
winget install UB-Mannheim.TesseractOCR
winget install oschwartz10612.Poppler
```

安装后重新打开 PowerShell，检查命令：

```powershell
where tesseract
where pdftoppm
```

中文扫描件需要 Tesseract 的 `chi_sim` 中文语言包。系统会优先使用：

```text
chi_sim+eng
```

如果中文语言包缺失，会自动退回英文 `eng`，但中文识别效果会很差。

## 查看 OCR 状态

启动项目后访问：

```text
http://localhost:5153/api/ocr/status
```

返回字段含义：

- `hasTesseract`：是否找到 Tesseract。
- `hasPopplerPdftoppm`：是否找到 Poppler 的 `pdftoppm`。
- `hasMuPdfMutool`：是否找到 MuPDF 的 `mutool`。
- `isReady`：是否满足 OCR 运行条件。

## 注意事项

- 普通文字型 PDF 不需要 OCR。
- 扫描版/图片型 PDF 需要 OCR。
- 如果没有安装 OCR 工具，系统仍会保存原文件并支持原文件预览，但 Agent 无法基于图片文字准确问答。
- OCR 最多处理前 20 页，避免大 PDF 上传后等待太久。
