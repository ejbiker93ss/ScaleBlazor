let dailyChart = null;
let timelineChart = null;

window.JSInterop = {
    updateDailyChart: function (data) {
        const ctx = document.getElementById('dailyChart');
        if (!ctx) return;

        const labels = data.map(d => {
            const date = new Date(d.date);
            return `${date.toLocaleDateString('en-US', { month: 'short' })} ${date.getDate()}`;
        }).reverse();

        const values = data.map(d => d.averageWeight).reverse();

        if (dailyChart) {
            dailyChart.destroy();
        }

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
                    }
                },
                scales: {
                    y: {
                        beginAtZero: false,
                        min: 44,
                        max: 47,
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
                        min: 44,
                        max: 47,
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
    }
};

// File download helper
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

// Reports page charts
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

    // Sample data - replace with actual API call
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

    // Use actual data from API
    const labels = data.map(d => d.label);
    const values = data.map(d => d.averageWeight);

    // Calculate dynamic min/max with some padding
    const minValue = values.length > 0 ? Math.min(...values) : 44;
    const maxValue = values.length > 0 ? Math.max(...values) : 47;
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
                    min: Math.floor(minValue - padding),
                    max: Math.ceil(maxValue + padding),
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

    // Sample data - replace with actual API call
    const labels = [];
    const values = [];
    for (let i = 0; i < 24; i++) {
        labels.push(`${i}:00`);
        values.push(i >= 8 && i <= 17 ? 45 + Math.random() * 1.5 : 0);
    }

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
                    min: 44,
                    max: 47
                }
            }
        }
    });
};

