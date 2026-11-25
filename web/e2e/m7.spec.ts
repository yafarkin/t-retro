import { expect, BrowserContext, Page, test } from '@playwright/test';
import { advanceBeforeEveryoneIsReady, closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

async function watchAudio(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    const target = window as unknown as { audioStarts: number; AudioContext: unknown };
    target.audioStarts = 0;
    target.AudioContext = class {
      constructor() {
        target.audioStarts++;
      }
    };
  });
}

async function startTimer(page: Page, minutes: string): Promise<void> {
  await page.getByRole('button', { name: 'Настроить таймер' }).click();
  await page.getByLabel('Минуты').fill(minutes);
  await page.getByRole('button', { name: 'Запустить таймер' }).click();
}

test('M7 silent timer countdown and expiration synchronize across contexts and tabs', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  await watchAudio(firstContext);
  await watchAudio(secondContext);
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm7-first', 'Первый M7');
  await login(second, origin, 'm7-second', 'Второй M7');
  await first.goto(`${origin}/meetings/new`);
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M7 таймер');
  await first.getByRole('button', { name: 'Создать ретро', exact: true }).click();
  await expect(first.getByRole('heading', { name: 'Ретро M7 таймер' })).toBeVisible();
  const meetingUrl = first.url();
  await first.getByRole('button', { name: 'Пригласить', exact: true }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await first.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Сбор карточек');
  const firstTab = await firstContext.newPage();
  await firstTab.goto(meetingUrl);
  await expect(firstTab.getByRole('heading', { name: 'Ретро M7 таймер' })).toBeVisible();

  await first.getByRole('button', { name: 'Настроить таймер' }).click();
  const timer = first.getByRole('dialog', { name: 'Длительность таймера' });
  await expect(timer.getByRole('button', { name: /^(1|5|10|15|30|60) мин$/ })).toHaveCount(6);
  await timer.getByRole('button', { name: '1 мин', exact: true }).click();
  await timer.getByRole('button', { name: 'Запустить таймер' }).click();
  for (const page of [first, firstTab, second]) {
    await expect(page.locator('.timer-countdown')).toHaveText(/^(00:[0-5]\d|01:00)$/);
  }
  await expect(first.getByText('Таймер запущен.', { exact: true })).toHaveCount(0);
  await expect(first.getByRole('button', { name: 'Управление таймером' })).toHaveText(/^\d{2}:\d{2}$/);

  await first.getByRole('button', { name: 'Управление таймером' }).click();
  await first.getByLabel('Минуты').fill('0.5');
  await first.getByRole('button', { name: 'Добавить время' }).click();
  await expect(second.locator('.timer-countdown')).toHaveText(/^01:(0[1-9]|[1-5]\d)$/);
  await expect(first.getByText('Время добавлено.', { exact: true })).toHaveCount(0);
  await first.getByRole('button', { name: 'Управление таймером' }).click();
  await first.getByRole('button', { name: 'Остановить таймер' }).click();
  await expect(second.locator('.timer-countdown')).toHaveCount(0);
  await expect(first.getByText('Таймер остановлен.', { exact: true })).toHaveCount(0);
  await expect(first.getByText('Время вышло', { exact: true })).toHaveCount(0);
  await expect(second.getByText('Время вышло', { exact: true })).toHaveCount(0);

  await startTimer(first, '0.02');
  for (const page of [first, firstTab, second]) {
    await expect(page.getByText('Время вышло', { exact: true })).toBeVisible({ timeout: 10_000 });
    expect(await page.evaluate(() => (window as unknown as { audioStarts: number }).audioStarts)).toBe(0);
  }

  const reconnectedTab = await firstContext.newPage();
  await reconnectedTab.goto(meetingUrl);
  await expect(reconnectedTab.getByRole('heading', { name: 'Ретро M7 таймер' })).toBeVisible();
  await expect(reconnectedTab.getByText('Время вышло', { exact: true })).toHaveCount(0);
  await expect(reconnectedTab.locator('.timer-countdown')).toHaveCount(0);

  await startTimer(first, '0.1');
  await expect(first.locator('.timer-countdown')).toBeVisible();
  await expect(first.getByText('Время вышло', { exact: true })).toHaveCount(0);
  await advanceBeforeEveryoneIsReady(first, 'Перейти к группировке');
  await expect(second.locator('.timer-countdown')).toHaveCount(0);
  await expect(second.getByText('Время вышло', { exact: true })).toHaveCount(0);

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
