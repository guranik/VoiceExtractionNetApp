/**
 * Клиентская проверка заголовка WAV-файла (без проверки RIFF)
 * Читает только первые ~44 байта, не загружая весь файл
 */
export function validateWavHeader(file) {
    return new Promise((resolve) => {
        // Быстрые проверки
        if (!file.name.toLowerCase().endsWith('.wav')) {
            resolve({ isValid: false, error: 'Ожидался файл с расширением .wav' });
            return;
        }

        if (file.size < 44) {
            resolve({ isValid: false, error: 'Файл слишком мал для корректного WAV' });
            return;
        }

        const reader = new FileReader();

        reader.onload = function (e) {
            try {
                const buffer = e.target.result;
                const view = new DataView(buffer);

                // Проверка WAVE-метки (на смещении 8)
                const wave = String.fromCharCode(
                    view.getUint8(8), view.getUint8(9),
                    view.getUint8(10), view.getUint8(11)
                );
                if (wave !== 'WAVE') {
                    resolve({ isValid: false, error: 'Отсутствует WAVE метка' });
                    return;
                }

                // Поиск чанка "fmt " (упрощённо: проверяем по фиксированному смещению)
                let fmtOffset = 12;
                let foundFmt = false;

                while (fmtOffset + 8 <= buffer.byteLength) {
                    const chunkId = String.fromCharCode(
                        view.getUint8(fmtOffset), view.getUint8(fmtOffset + 1),
                        view.getUint8(fmtOffset + 2), view.getUint8(fmtOffset + 3)
                    );
                    const chunkSize = view.getUint32(fmtOffset + 4, true); // little-endian

                    if (chunkId === 'fmt ') {
                        foundFmt = true;

                        // Чтение параметров: format tag (2), channels (2), sample rate (4)
                        const formatTag = view.getUint16(fmtOffset + 8, true);
                        const channels = view.getUint16(fmtOffset + 10, true);
                        const sampleRate = view.getUint32(fmtOffset + 12, true);

                        if (formatTag !== 1) {
                            resolve({ isValid: false, error: 'Поддерживается только PCM-формат' });
                            return;
                        }
                        if (channels !== 1) {
                            resolve({ isValid: false, error: `Поддерживается только моно (1 канал), получено: ${channels}` });
                            return;
                        }
                        if (sampleRate !== 8000 && sampleRate !== 16000) {
                            resolve({ isValid: false, error: `Поддерживается 8 или 16 кГц, получено: ${sampleRate} Гц` });
                            return;
                        }

                        resolve({ isValid: true, error: null });
                        return;
                    }

                    // Переход к следующему чанку
                    fmtOffset += 8 + chunkSize;
                    if (chunkSize % 2 !== 0) fmtOffset++; // Выравнивание по слову
                }

                if (!foundFmt) {
                    resolve({ isValid: false, error: 'Не найден блок fmt' });
                    return;
                }

            } catch (err) {
                resolve({ isValid: false, error: `Ошибка чтения: ${err.message}` });
            }
        };

        reader.onerror = function () {
            resolve({ isValid: false, error: 'Не удалось прочитать файл' });
        };

        // Читаем только первые 256 байт (достаточно для заголовка + fmt чанка)
        const blob = file.slice(0, Math.min(256, file.size));
        reader.readAsArrayBuffer(blob);
    });
}