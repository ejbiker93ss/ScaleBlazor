let dailyChart = null;
let timelineChart = null;
let readingSuccessAudio = null;
let readingSuccessUnlocked = false;

const getReadingSuccessAudio = (url) => {
    if (!readingSuccessAudio) {
        readingSuccessAudio = new Audio(url);
        readingSuccessAudio.preload = 'auto';
    }

    if (readingSuccessAudio.src !== new URL(url, window.location.href).href) {
        readingSuccessAudio.src = url;
    }

    return readingSuccessAudio;
};

window.JSInterop = {
    updateDailyChart: function (data) {
        const ctx = document.getElementById('dailyChart');
        if (!ctx) return;

        const labels = data.map(d => {
            const date = new Date(d.date);
            return `${date.toLocaleDateString('en-US', { month: 'short' })} ${date.getDate()}`;
        }).reverse();

        const values = data.map(d => d.averageWeight).reverse();
        const palletCounts = data.map(d => d.palletCount ?? 0).reverse();

        if (dailyChart) {
            dailyChart.destroy();
        }

        const minValue = values.length > 0 ? Math.min(...values) : 34;
        const maxValue = values.length > 0 ? Math.max(...values) : 55;
        const padding = (maxValue - minValue) * 0.1 || 1;

        const isSmallScreen = window.innerWidth <= 900 && window.innerHeight <= 450;

        dailyChart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    data: values,
                    backgroundColor: '#FF6B35',
                    borderRadius: isSmallScreen ? 1 : 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: false
                    },
                    tooltip: {
                        callbacks: {
                            label: function(context) {
                                const count = palletCounts[context.dataIndex] ?? 0;
                                return `Avg: ${context.parsed.y.toFixed(2)} lbs (${count} pallets)`;
                            }
                        }
                    }
                },
                scales: {
                    y: {
                        beginAtZero: false,
                        min: minValue - padding,
                        max: maxValue + padding,
                        ticks: {
                            stepSize: 1,
                            font: {
                                size: isSmallScreen ? 5 : 12
                            }
                        },
                        grid: {
                            display: !isSmallScreen
                        }
                    },
                    x: {
                        grid: {
                            display: false
                        },
                        ticks: {
                            font: {
                                size: isSmallScreen ? 5 : 11
                            }
                        }
                    }
                },
                layout: {
                    padding: isSmallScreen ? 0 : 10
                }
            }
        });
    },

    updateTimelineChart: function (data) {
        const ctx = document.getElementById('timelineChart');
        if (!ctx) return;

        const labels = data.map(d => {
            const date = new Date(d.timestamp);
            return date.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
        });

        const values = data.map(d => d.weight);

        if (timelineChart) {
            timelineChart.destroy();
        }

        const minValue = values.length > 0 ? Math.min(...values) : 35;
        const maxValue = values.length > 0 ? Math.max(...values) : 55;
        const padding = (maxValue - minValue) * 0.1 || 1;

        const isSmallScreen = window.innerWidth <= 900 && window.innerHeight <= 450;

        timelineChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [{
                    data: values,
                    borderColor: '#4FC3F7',
                    backgroundColor: 'rgba(79, 195, 247, 0.1)',
                    tension: 0.4,
                    fill: true,
                    pointBackgroundColor: '#4FC3F7',
                    pointBorderColor: '#4FC3F7',
                    pointRadius: isSmallScreen ? 1 : 4,
                    borderWidth: isSmallScreen ? 1 : 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: false
                    }
                },
                scales: {
                    y: {
                        beginAtZero: false,
                        min: minValue - padding,
                        max: maxValue + padding,
                        ticks: {
                            stepSize: 1,
                            font: {
                                size: isSmallScreen ? 5 : 12
                            }
                        },
                        grid: {
                            display: !isSmallScreen
                        }
                    },
                    x: {
                        grid: {
                            color: '#e0e0e0',
                            display: !isSmallScreen
                        },
                        ticks: {
                            font: {
                                size: isSmallScreen ? 5 : 11
                            },
                            maxRotation: isSmallScreen ? 45 : 0,
                            minRotation: isSmallScreen ? 45 : 0
                        }
                    }
                },
                layout: {
                    padding: isSmallScreen ? 0 : 10
                }
            }
        });
    },

    initializeSound: function (url) {
        const audio = getReadingSuccessAudio(url);

        if (readingSuccessUnlocked) {
            return;
        }

        const unlock = () => {
            if (readingSuccessUnlocked) {
                return;
            }

            audio.muted = true;
            audio.play().then(() => {
                audio.pause();
                audio.currentTime = 0;
                audio.muted = false;
                readingSuccessUnlocked = true;
            }).catch(() => {
            });
        };

        document.addEventListener('pointerdown', unlock, { once: true });
    },

    primeSound: function (url) {
        const audio = getReadingSuccessAudio(url);
        audio.muted = true;
        return audio.play().then(() => {
            audio.pause();
            audio.currentTime = 0;
            audio.muted = false;
            readingSuccessUnlocked = true;
        }).catch(() => {
        });
    },

    playSound: function (url) {
        const audio = getReadingSuccessAudio(url);
        audio.currentTime = 0;
        audio.play().then(() => {
            readingSuccessUnlocked = true;
        }).catch(() => {
        });
    },

    exitKiosk: function () {
        const doc = document;
        const isFullscreen = doc.fullscreenElement || doc.webkitFullscreenElement || doc.mozFullScreenElement || doc.msFullscreenElement;
        if (!isFullscreen) {
            return;
        }

        const exitFullscreen = doc.exitFullscreen || doc.webkitExitFullscreen || doc.mozCancelFullScreen || doc.msExitFullscreen;
        if (exitFullscreen) {
            return exitFullscreen.call(doc);
        }
    }
};

if (!window.JSInterop.exitKiosk) {
    window.JSInterop.exitKiosk = function () {
        const doc = document;
        const isFullscreen = doc.fullscreenElement || doc.webkitFullscreenElement || doc.mozFullScreenElement || doc.msFullscreenElement;
        if (!isFullscreen) {
            return;
        }

        const exitFullscreen = doc.exitFullscreen || doc.webkitExitFullscreen || doc.mozCancelFullScreen || doc.msExitFullscreen;
        if (exitFullscreen) {
            return exitFullscreen.call(doc);
        }
    };
}

window.downloadFile = async function (url, filename) {
    try {
        const response = await fetch(url);
        const blob = await response.blob();
        const link = document.createElement('a');
        link.href = window.URL.createObjectURL(blob);
        link.download = filename;
        link.click();
        window.URL.revokeObjectURL(link.href);
    } catch (error) {
        console.error('Error downloading file:', error);
        throw error;
    }
};

let averageWeightChart = null;
let trendsChart = null;
let hourlyChart = null;

window.renderAverageWeightChart = function (data, viewType) {
    const ctx = document.getElementById('averageWeightChart');
    if (!ctx) return;

    if (averageWeightChart) {
        averageWeightChart.destroy();
    }

    const labels = data.map(d => d.label);
    const values = data.map(d => d.averageWeight);

    const minValue = values.length > 0 ? Math.min(...values) : 35;
    const maxValue = values.length > 0 ? Math.max(...values) : 55;
    const padding = (maxValue - minValue) * 0.1 || 1;

    const title = viewType === 'weekly' ? 'Weekly Average Weight' : 'Daily Average Weight';

    averageWeightChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: title,
                data: values,
                borderColor: '#667eea',
                backgroundColor: 'rgba(102, 126, 234, 0.1)',
                tension: 0.4,
                fill: true,
                pointRadius: 4,
                pointHoverRadius: 6,
                borderWidth: 3
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: true,
                    position: 'top'
                },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            return context.dataset.label + ': ' + context.parsed.y.toFixed(2) + ' lbs';
                        }
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: false,
                    min: minValue - padding,
                    max: maxValue + padding,
                    ticks: {
                        callback: function(value) {
                            return value.toFixed(1) + ' lbs';
                        },
                        font: { size: 12 }
                    },
                    title: {
                        display: true,
                        text: 'Weight (lbs)'
                    }
                },
                x: {
                    ticks: {
                        font: { size: 11 },
                        maxRotation: 45,
                        minRotation: 45
                    }
                }
            }
        }
    });
};

window.renderDistributionChart = function () {
    const ctx = document.getElementById('distributionChart');
    if (!ctx) return;

    if (distributionChart) {
        distributionChart.destroy();
    }

    const labels = ['44.0-44.5', '44.5-45.0', '45.0-45.5', '45.5-46.0', '46.0-46.5'];
    const values = [15, 45, 120, 80, 25];

    distributionChart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Frequency',
                data: values,
                backgroundColor: '#667eea',
                borderRadius: 6
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: false
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: {
                        font: { size: 12 }
                    }
                },
                x: {
                    ticks: {
                        font: { size: 11 }
                    }
                }
            }
        }
    });
};

window.renderTrendsChart = function (data) {
    const ctx = document.getElementById('trendsChart');
    if (!ctx) return;

    if (trendsChart) {
        trendsChart.destroy();
    }

    const labels = data.map(d => d.label);
    const values = data.map(d => d.averageWeight);

    const minValue = values.length > 0 ? Math.min(...values) : 35;
    const maxValue = values.length > 0 ? Math.max(...values) : 55;
    const padding = (maxValue - minValue) * 0.1 || 1;

    trendsChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'Average Weight (lbs)',
                data: values,
                borderColor: '#667eea',
                backgroundColor: 'rgba(102, 126, 234, 0.1)',
                tension: 0.4,
                fill: true,
                pointRadius: 3,
                pointHoverRadius: 6,
                borderWidth: 2
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: true,
                    position: 'top'
                },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            return 'Avg Weight: ' + context.parsed.y.toFixed(2) + ' lbs';
                        }
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: false,
                    min: minValue - padding,
                    max: maxValue + padding,
                    ticks: {
                        callback: function(value) {
                            return value.toFixed(1) + ' lbs';
                        }
                    },
                    title: {
                        display: true,
                        text: 'Weight (lbs)'
                    }
                },
                x: {
                    ticks: {
                        maxRotation: 45,
                        minRotation: 45,
                        font: { size: 10 }
                    }
                }
            }
        }
    });
};

window.renderHourlyChart = function () {
    const ctx = document.getElementById('hourlyChart');
    if (!ctx) return;

    if (hourlyChart) {
        hourlyChart.destroy();
    }

    const labels = [];
    const values = [];
    for (let i = 0; i < 24; i++) {
        labels.push(`${i}:00`);
        values.push(i >= 8 && i <= 17 ? 45 + Math.random() * 1.5 : 0);
    }

    const minValue = values.length > 0 ? Math.min(...values) : 35;
    const maxValue = values.length > 0 ? Math.max(...values) : 55;
    const padding = (maxValue - minValue) * 0.1 || 1;

    hourlyChart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Average Weight (lbs)',
                data: values,
                backgroundColor: '#4FC3F7',
                borderRadius: 4
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: false
                }
            },
            scales: {
                y: {
                    beginAtZero: false,
                    min: minValue - padding,
                    max: maxValue + padding
                }
            }
        }
    });
};
