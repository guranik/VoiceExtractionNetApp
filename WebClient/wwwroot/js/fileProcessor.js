document.addEventListener('DOMContentLoaded', function () {
    const form = document.getElementById('processingForm');
    const startBtn = document.getElementById('startBtn');
    const resultMessage = document.getElementById('resultMessage');
    const downloadSection = document.getElementById('downloadSection');
    const downloadLink = document.getElementById('downloadLink');

    let pollingInterval = null;
    let currentSessionId = null;
    let originalFileName = null;

    form.addEventListener('submit', async function (e) {
        e.preventDefault();

        const fileInput = document.getElementById('sourceFile');
        const file = fileInput.files[0];

        if (!file) {
            showAlert('Пожалуйста, выберите файл', 'warning');
            return;
        }

        originalFileName = file.name;
        setProcessingState(true);
        hideDownloadSection();

        // 🔹 АВТО-КОНВЕРТАЦИЯ ФАЙЛА
        showAlert('🔧 Подготовка аудио (конвертация в WAV)...', 'info');

        try {
            const processedFile = await convertToWav(file);

            showAlert('✅ Файл готов. Загрузка на сервер...', 'info');

            const formData = new FormData();
            formData.append('file', processedFile.blob, processedFile.name);

            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (token) {
                formData.append('__RequestVerificationToken', token);
            }

            const response = await fetch('?handler=Upload', {
                method: 'POST',
                body: formData,
                headers: {
                    'RequestVerificationToken': token || ''
                }
            });

            if (!response.ok) {
                const errorData = await safeParseJson(response);
                throw new Error(errorData?.error || `HTTP ${response.status}`);
            }

            const data = await response.json();
            currentSessionId = data.sessionId;

            showAlert('✅ Обработка запущена. Ожидание прогресса...', 'info');
            startPolling(currentSessionId);

        } catch (error) {
            console.error('Ошибка:', error);
            showAlert(`❌ ${error.message}`, 'danger');
            setProcessingState(false);
        }
    });

    // === Остальные функции (polling, download, progress) без изменений ===
    // ... (код из предыдущей версии) ...

    function startPolling(sessionId) {
        if (pollingInterval) clearInterval(pollingInterval);
        pollingInterval = setInterval(async () => {
            try {
                const response = await fetch(`?handler=Progress&sessionId=${encodeURIComponent(sessionId)}`);
                if (!response.ok) return;

                const contentType = response.headers.get('content-type')?.toLowerCase() || '';
                const contentDisposition = response.headers.get('content-disposition') || '';
                const isBinary = contentType.includes('application/octet-stream') ||
                    contentType.includes('binary/octet-stream') ||
                    contentDisposition.includes('filename') ||
                    contentDisposition.includes('attachment');

                if (isBinary) {
                    await handleFileDownload(response);
                    stopPolling();
                    return;
                }

                const result = await response.json();
                if (result.type === 'progress' && result.data) {
                    updateProgressBars(result.data);
                } else if (result.type === 'file') {
                    await handleFileDownload(response);
                    stopPolling();
                }
            } catch (error) {
                console.error('Ошибка polling:', error);
            }
        }, 3000);
    }

    async function handleFileDownload(response) {
        try {
            const contentDisposition = response.headers.get('content-disposition') || '';
            let fileName = extractFileNameFromHeader(contentDisposition) ||
                originalFileName?.replace(/\.[^/.]+$/, '') + '_processed.wav' ||
                'result.wav';

            const blob = await response.blob();
            const downloadUrl = window.URL.createObjectURL(blob);

            if (downloadLink) {
                downloadLink.href = downloadUrl;
                downloadLink.download = fileName;
                showDownloadSection(fileName);
            } else {
                triggerAutoDownload(downloadUrl, fileName);
            }

            updateProgressBar('extractProgress', 'extractStatus', 100);
            updateProgressBar('transcribeProgress', 'transcribeStatus', 100);

            showAlert(`✅ Обработка завершена! Файл <strong>${fileName}</strong> готов.`, 'success');
            setProcessingState(false);
            setTimeout(() => window.URL.revokeObjectURL(downloadUrl), 60000);
        } catch (error) {
            console.error('Ошибка скачивания:', error);
            showAlert('⚠️ Файл обработан, но не удалось подготовить скачивание.', 'warning');
            setProcessingState(false);
        }
    }

    function extractFileNameFromHeader(header) {
        if (!header) return null;
        const filenameStar = header.match(/filename\*=UTF-8''([^;]+)/i);
        if (filenameStar?.[1]) return decodeURIComponent(filenameStar[1]);
        const filename = header.match(/filename=["']?([^;"'\r\n]+)["']?/i);
        return filename?.[1]?.trim() || null;
    }

    function triggerAutoDownload(url, fileName) {
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    }

    function updateProgressBars(data) {
        const { earliestExtractSegmentStart, inputFileDuration, latestTranscriptionEnd } = data;
        if (inputFileDuration > 0 && earliestExtractSegmentStart !== undefined) {
            const percent = Math.min(100, Math.max(0,
                Math.round((earliestExtractSegmentStart / inputFileDuration) * 100)));
            updateProgressBar('extractProgress', 'extractStatus', percent);
        }
        if (inputFileDuration > 0 && latestTranscriptionEnd !== undefined) {
            const percent = Math.min(100, Math.max(0,
                Math.round((latestTranscriptionEnd / inputFileDuration) * 100)));
            updateProgressBar('transcribeProgress', 'transcribeStatus', percent);
        }
    }

    function updateProgressBar(barId, statusId, percent) {
        const bar = document.getElementById(barId);
        const status = document.getElementById(statusId);
        if (bar) {
            bar.style.width = `${percent}%`;
            bar.textContent = `${percent}%`;
            bar.setAttribute('aria-valuenow', percent);
        }
        if (status) {
            status.textContent = percent >= 100 ? '✅ Завершено' : `Выполнено: ${percent}%`;
            status.classList.toggle('text-success', percent >= 100);
        }
    }

    function stopPolling() {
        if (pollingInterval) {
            clearInterval(pollingInterval);
            pollingInterval = null;
        }
    }

    function setProcessingState(isProcessing) {
        startBtn.disabled = isProcessing;
        startBtn.innerHTML = isProcessing
            ? '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Обработка...'
            : 'Начать обработку';
        document.getElementById('sourceFile').disabled = isProcessing;
        const folderInput = document.getElementById('outputFolder');
        if (folderInput) folderInput.disabled = isProcessing;
    }

    function showAlert(message, type) {
        if (!resultMessage) return;
        resultMessage.className = `alert alert-${type} mt-3`;
        resultMessage.innerHTML = message;
        resultMessage.classList.remove('d-none');
        resultMessage.setAttribute('role', 'alert');
        if (type === 'info') {
            setTimeout(() => resultMessage?.classList.add('d-none'), 4000);
        }
    }

    function showDownloadSection(fileName) {
        if (!downloadSection || !downloadLink) return;
        downloadLink.textContent = `📥 Скачать: ${fileName}`;
        downloadSection.classList.remove('d-none');
    }

    function hideDownloadSection() {
        downloadSection?.classList.add('d-none');
    }

    async function safeParseJson(response) {
        try {
            const text = await response.text();
            return text ? JSON.parse(text) : null;
        } catch {
            return null;
        }
    }

    window.addEventListener('beforeunload', () => {
        stopPolling();
        document.querySelectorAll('a[href^="blob:"]').forEach(a => window.URL.revokeObjectURL(a.href));
    });
});

/**
 * 🔹 🔥 ГЛАВНАЯ ФУНКЦИЯ: Конвертирует ЛЮБОЕ аудио в правильный WAV
 * 
 * Что делает:
 * 1. Декодирует любой формат (MP3, OGG, FLAC, WAV float, etc.)
 * 2. Конвертирует в Mono
 * 3. Ресемплирует в 16000 Гц
 * 4. Создаёт правильный RIFF/WAVE заголовок с PCM 16-bit
 * 
 * @param {File} file - Исходный файл
 * @returns {Promise<{blob: Blob, name: string}>} - Готовый WAV файл
 */
async function convertToWav(file) {
    const arrayBuffer = await file.arrayBuffer();
    const audioContext = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: 16000 });

    try {
        // 1. Декодируем аудио (браузер сам разберётся с форматом)
        const audioBuffer = await audioContext.decodeAudioData(arrayBuffer.slice(0));

        // 2. Конвертируем в Mono (если стерео)
        const monoBuffer = audioContext.createBuffer(1, audioBuffer.length, audioBuffer.sampleRate);
        const channelData = monoBuffer.getChannelData(0);

        if (audioBuffer.numberOfChannels === 1) {
            channelData.set(audioBuffer.getChannelData(0));
        } else {
            // Микшируем стерео в моно (L + R) / 2
            const left = audioBuffer.getChannelData(0);
            const right = audioBuffer.getChannelData(1);
            for (let i = 0; i < channelData.length; i++) {
                channelData[i] = (left[i] + right[i]) / 2;
            }
        }

        // 3. Создаём правильный WAV с заголовками
        const wavBlob = audioBufferToWav(monoBuffer, 16000);
        const newName = file.name.replace(/\.[^/.]+$/, '') + '_converted.wav';

        return { blob: wavBlob, name: newName };

    } finally {
        await audioContext.close();
    }
}

/**
 * 🔹 Создаёт правильный RIFF/WAVE файл из AudioBuffer
 * 
 * Структура WAV:
 * [RIFF header] [fmt chunk] [data chunk]
 * 
 * @param {AudioBuffer} audioBuffer - Декодированное аудио
 * @param {number} sampleRate - Частота дискретизации (16000)
 * @returns {Blob} - WAV файл с правильными заголовками
 */
function audioBufferToWav(audioBuffer, sampleRate) {
    const numChannels = 1;
    const bitsPerSample = 16;
    const bytesPerSample = bitsPerSample / 8;
    const blockAlign = numChannels * bytesPerSample;
    const byteRate = sampleRate * blockAlign;
    const dataSize = audioBuffer.length * blockAlign;
    const bufferSize = 44 + dataSize;

    const buffer = new ArrayBuffer(bufferSize);
    const view = new DataView(buffer);

    // === RIFF HEADER (12 байт) ===
    writeString(view, 0, 'RIFF');                    // 0-3: Chunk ID
    view.setUint32(4, 36 + dataSize, true);          // 4-7: Chunk Size
    writeString(view, 8, 'WAVE');                    // 8-11: Format

    // === fmt CHUNK (24 байта) ===
    writeString(view, 12, 'fmt ');                   // 12-15: Subchunk1 ID
    view.setUint32(16, 16, true);                    // 16-19: Subchunk1 Size (16 для PCM)
    view.setUint16(20, 1, true);                     // 20-21: Audio Format (1 = PCM)
    view.setUint16(22, numChannels, true);           // 22-23: Num Channels (1 = Mono)
    view.setUint32(24, sampleRate, true);            // 24-27: Sample Rate
    view.setUint32(28, byteRate, true);              // 28-31: Byte Rate
    view.setUint16(32, blockAlign, true);            // 32-33: Block Align
    view.setUint16(34, bitsPerSample, true);         // 34-35: Bits Per Sample

    // === data CHUNK ===
    writeString(view, 36, 'data');                   // 36-39: Subchunk2 ID
    view.setUint32(40, dataSize, true);              // 40-43: Subchunk2 Size

    // === Аудиоданные (16-bit PCM) ===
    const channelData = audioBuffer.getChannelData(0);
    let offset = 44;

    for (let i = 0; i < channelData.length; i++) {
        // Конвертируем float (-1.0 to 1.0) в int16 (-32768 to 32767)
        const sample = Math.max(-1, Math.min(1, channelData[i]));
        const int16 = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
        view.setInt16(offset, int16, true);
        offset += 2;
    }

    return new Blob([buffer], { type: 'audio/wav' });
}

/**
 * Вспомогательная функция для записи строк в DataView
 */
function writeString(view, offset, string) {
    for (let i = 0; i < string.length; i++) {
        view.setUint8(offset + i, string.charCodeAt(i));
    }
}