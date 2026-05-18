document.addEventListener('DOMContentLoaded', function () {
    const form = document.getElementById('processingForm');
    const startBtn = document.getElementById('startBtn');
    const resultMessage = document.getElementById('resultMessage');
    const downloadSection = document.getElementById('downloadSection');
    const downloadLink = document.getElementById('downloadLink');

    let pollingInterval = null;
    let pollingInProgress = false;
    let currentSessionId = null;
    let originalFileName = null;

    const SESSION_STORAGE_KEY = 'audioProcessingSession';
    const SESSION_MAX_AGE_MS = 60 * 1000;

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
            saveSessionToStorage({
                sessionId: data.sessionId,
                originalFileName: originalFileName,
                startTime: Date.now()
            });
            startPolling(currentSessionId);

        } catch (error) {
            console.error('Ошибка:', error);
            showAlert(`❌ ${error.message}`, 'danger');
            setProcessingState(false);
        }
    });


    function startPolling(sessionId) {
        if (pollingInterval) clearInterval(pollingInterval);
        pollingInterval = setInterval(async () => {

            if (pollingInProgress)
                return;

            pollingInProgress = true;

            try {
                const controller = new AbortController();

                const timeoutId = setTimeout(() => {
                    controller.abort();
                }, 10000);

                const response = await fetch(
                    `?handler=Progress&sessionId=${encodeURIComponent(sessionId)}`,
                    {
                        signal: controller.signal
                    });

                clearTimeout(timeoutId);

                if (!response.ok)
                    return;

                const contentType = response.headers.get('content-type')?.toLowerCase() || '';
                const contentDisposition = response.headers.get('content-disposition') || '';

                const isBinary =
                    contentType.includes('application/octet-stream') ||
                    contentType.includes('binary/octet-stream') ||
                    contentDisposition.includes('filename') ||
                    contentDisposition.includes('attachment');

                if (isBinary) {
                    await handleFileDownload(response);
                    stopPolling();
                    return;
                }

                const result = await response.json();

                if (result.type === 'progress' && result.data)
                    updateProgressBars(result.data);
            }
            catch (error) {
                console.warn('Polling reconnect...', error);
            }
            finally {
                pollingInProgress = false;
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
            setTimeout(() => window.URL.revokeObjectURL(downloadUrl), 1800000);

            clearSessionStorage();
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

    function saveSessionToStorage(sessionData) {
        try {
            localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(sessionData));
        } catch (e) {
            console.warn('Не удалось сохранить сессию в localStorage:', e);
        }
    }

    function loadSessionFromStorage() {
        try {
            const raw = localStorage.getItem(SESSION_STORAGE_KEY);
            if (!raw) return null;

            const session = JSON.parse(raw);
            const age = Date.now() - session.startTime;

            if (age > SESSION_MAX_AGE_MS) {
                clearSessionStorage();
                return null;
            }
            return session;
        } catch {
            clearSessionStorage();
            return null;
        }
    }

    function clearSessionStorage() {
        try {
            localStorage.removeItem(SESSION_STORAGE_KEY);
        } catch { }
    }

    async function safeParseJson(response) {
        try {
            const text = await response.text();
            return text ? JSON.parse(text) : null;
        } catch {
            return null;
        }
    }

    (async function resumeSessionIfAny() {
        const saved = loadSessionFromStorage();
        if (!saved?.sessionId) return;

        // Проверяем, не завершена ли сессия уже
        try {
            const response = await fetch(`?handler=Progress&sessionId=${encodeURIComponent(saved.sessionId)}`, {
                method: 'GET',
                cache: 'no-store'
            });

            const contentType = response.headers.get('content-type')?.toLowerCase() || '';
            const isBinary = contentType.includes('application/octet-stream') ||
                response.headers.get('content-disposition')?.includes('filename');

            if (isBinary) {
                // Файл готов — сразу показываем скачивание
                originalFileName = saved.originalFileName;
                await handleFileDownload(response);
                clearSessionStorage();
                showAlert(`✅ Обработка завершена пока вы были в офлайне. Файл <strong>${saved.originalFileName}</strong> готов.`, 'success');
                return;
            }

            // Сессия активна — возобновляем поллинг
            currentSessionId = saved.sessionId;
            originalFileName = saved.originalFileName;
            setProcessingState(true);
            showAlert('🔄 Обнаружена активная сессия. Восстановление прогресса...', 'info');
            startPolling(currentSessionId);

        } catch (err) {
            console.warn('Не удалось восстановить сессию:', err);
            clearSessionStorage();
        }
    })();

    window.addEventListener('beforeunload', () => {
        stopPolling();
        document.querySelectorAll('a[href^="blob:"]').forEach(a => window.URL.revokeObjectURL(a.href));
    });
});


async function convertToWav(file) {
    if (file.name.toLowerCase().endsWith('.wav')) {
        const headerCheck = await checkWavHeader(file);
        if (headerCheck.isValid) {
            return { blob: file, name: file.name };
        }
    }
    const arrayBuffer = await file.arrayBuffer();
    const audioContext = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: 16000 });

    try {
        const audioBuffer = await audioContext.decodeAudioData(arrayBuffer.slice(0));

        const monoBuffer = audioContext.createBuffer(1, audioBuffer.length, audioBuffer.sampleRate);
        const channelData = monoBuffer.getChannelData(0);

        if (audioBuffer.numberOfChannels === 1) {
            channelData.set(audioBuffer.getChannelData(0));
        } else {
            const left = audioBuffer.getChannelData(0);
            const right = audioBuffer.getChannelData(1);
            for (let i = 0; i < channelData.length; i++) {
                channelData[i] = (left[i] + right[i]) / 2;
            }
        }

        const wavBlob = audioBufferToWav(monoBuffer, 16000);
        const newName = file.name.replace(/\.[^/.]+$/, '') + '.wav';

        return { blob: wavBlob, name: newName };

    } finally {
        await audioContext.close();
    }
}


async function checkWavHeader(file) {
    return new Promise((resolve) => {
        if (file.size < 44) {
            resolve({ isValid: false });
            return;
        }

        const reader = new FileReader();
        reader.onload = function (e) {
            try {
                const buffer = e.target.result;
                const view = new DataView(buffer);

                const wave = String.fromCharCode(
                    view.getUint8(8), view.getUint8(9),
                    view.getUint8(10), view.getUint8(11)
                );
                if (wave !== 'WAVE') {
                    resolve({ isValid: false });
                    return;
                }

                let fmtOffset = 12;
                while (fmtOffset + 8 <= buffer.byteLength) {
                    const chunkId = String.fromCharCode(
                        view.getUint8(fmtOffset), view.getUint8(fmtOffset + 1),
                        view.getUint8(fmtOffset + 2), view.getUint8(fmtOffset + 3)
                    );
                    const chunkSize = view.getUint32(fmtOffset + 4, true);

                    if (chunkId === 'fmt ') {
                        const formatTag = view.getUint16(fmtOffset + 8, true);
                        const channels = view.getUint16(fmtOffset + 10, true);
                        const sampleRate = view.getUint32(fmtOffset + 12, true);
                        const bitsPerSample = view.getUint16(fmtOffset + 34, true);

                        const isValid = formatTag === 1 &&
                            channels === 1 &&
                            (sampleRate === 8000 || sampleRate === 16000) &&
                            bitsPerSample === 16;

                        resolve({ isValid, sampleRate });
                        return;
                    }

                    fmtOffset += 8 + chunkSize;
                    if (chunkSize % 2 !== 0) fmtOffset++;
                }

                resolve({ isValid: false });
            } catch {
                resolve({ isValid: false });
            }
        };
        reader.onerror = () => resolve({ isValid: false });

        const blob = file.slice(0, Math.min(256, file.size));
        reader.readAsArrayBuffer(blob);
    });
}


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

    writeString(view, 0, 'RIFF');                    
    view.setUint32(4, 36 + dataSize, true);          
    writeString(view, 8, 'WAVE');                    

    writeString(view, 12, 'fmt ');                   
    view.setUint32(16, 16, true);                    
    view.setUint16(20, 1, true);                     
    view.setUint16(22, numChannels, true);           
    view.setUint32(24, sampleRate, true);            
    view.setUint32(28, byteRate, true);              
    view.setUint16(32, blockAlign, true);            
    view.setUint16(34, bitsPerSample, true);         

    writeString(view, 36, 'data');                   
    view.setUint32(40, dataSize, true);              

    const channelData = audioBuffer.getChannelData(0);
    let offset = 44;

    for (let i = 0; i < channelData.length; i++) {
        const sample = Math.max(-1, Math.min(1, channelData[i]));
        const int16 = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
        view.setInt16(offset, int16, true);
        offset += 2;
    }

    return new Blob([buffer], { type: 'audio/wav' });
}


function writeString(view, offset, string) {
    for (let i = 0; i < string.length; i++) {
        view.setUint8(offset + i, string.charCodeAt(i));
    }
}