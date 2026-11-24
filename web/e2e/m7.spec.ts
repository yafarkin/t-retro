import { expect, BrowserContext, Page, test } from '@playwright/test';
import { closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

async function installAudioCounter(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    const target = window as unknown as { __timerBeeps: number; AudioContext: unknown };
    target.__timerBeeps = 0;
    target.AudioContext = class {
      readonly currentTime = 0;
      readonly destination = {};
      async resume(): Promise<void> {}
      async close(): Promise<void> {}
      createOscillator() {
        return {
          type: 'sine',
          frequency: { value: 0 },
          connect: () => undefined,
          start: () => { target.__timerBeeps += 1; },
          stop: () => undefined,
        };
      }
    };
  });
}

async function beepCount(page: Page): Promise<number> {
  return page.evaluate(() => (window as unknown as { __timerBeeps: number }).__timerBeeps);
}

async function resetBeeps(page: Page): Promise<void> {
  await page.evaluate(() => { (window as unknown as { __timerBeeps: number }).__timerBeeps = 0; });
}

test('M7 timer countdown, expiration, sound coordination and reconnect work across contexts and tabs', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  await installAudioCounter(firstContext);
  await installAudioCounter(secondContext);
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm7-first', 'Первый M7');
  await login(second, origin, 'm7-second', 'Второй M7');
  await first.goto(`${origin}/meetings/new`);
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M7 таймер');
  await first.getByRole('button', { name: 'Создать встречу' }).click();
  await expect(first.getByRole('heading', { name: 'Ретро M7 таймер' })).toBeVisible();
  const meetingUrl = first.url();
  await first.getByRole('button', { name: 'Показать приглашение' }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await first.getByRole('button', { name: 'Начать сбор' }).click();
  await expect(second.getByText('Сбор карточек', { exact: true })).toBeVisible();
  const firstTab = await firstContext.newPage();
  await firstTab.goto(meetingUrl);
  await expect(firstTab.getByRole('heading', { name: 'Ретро M7 таймер' })).toBeVisible();

  for (const page of [first, firstTab, second]) {
    await page.getByRole('button', { name: 'Проверить звук' }).click();
    await expect(page.getByText('Звук включён', { exact: true })).toBeVisible();
    await resetBeeps(page);
  }

  await first.getByLabel('Секунды таймера').fill('60');
  await first.getByRole('button', { name: 'Запустить таймер' }).click();
  await expect(second.locator('.timer-section')).toContainText(/Осталось: \d{2}:\d{2}/);
  await expect(firstTab.locator('.timer-section')).toContainText(/Осталось: \d{2}:\d{2}/);
  await first.getByLabel('Секунды таймера').fill('30');
  await first.getByRole('button', { name: 'Добавить время' }).click();
  await expect(first.getByText('Время добавлено.', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Остановить таймер' }).click();
  await expect(second.locator('.timer-section')).toContainText('Таймер не запущен.');
  await expect(first.getByText('Время вышло', { exact: true })).toHaveCount(0);
  await expect(second.getByText('Время вышло', { exact: true })).toHaveCount(0);
  expect(await beepCount(first)).toBe(0);
  expect(await beepCount(firstTab)).toBe(0);
  expect(await beepCount(second)).toBe(0);

  await first.getByLabel('Секунды таймера').fill('1');
  await first.getByRole('button', { name: 'Запустить таймер' }).click();
  for (const page of [first, firstTab, second]) {
    await expect(page.getByText('Время вышло', { exact: true })).toBeVisible({ timeout: 10_000 });
  }
  expect(await beepCount(first) + await beepCount(firstTab)).toBe(1);
  expect(await beepCount(second)).toBe(1);

  const reconnectedTab = await firstContext.newPage();
  await reconnectedTab.goto(meetingUrl);
  await expect(reconnectedTab.getByRole('heading', { name: 'Ретро M7 таймер' })).toBeVisible();
  await expect(reconnectedTab.getByText('Время вышло', { exact: true })).toHaveCount(0);
  await expect(reconnectedTab.locator('.timer-section')).toContainText('Таймер не запущен.');

  await first.getByLabel('Секунды таймера').fill('5');
  await first.getByRole('button', { name: 'Запустить таймер' }).click();
  await expect(first.getByText('Время вышло', { exact: true })).toHaveCount(0);
  await first.getByRole('button', { name: 'Перейти к группировке' }).click();
  await expect(second.locator('.timer-section')).toContainText('Таймер не запущен.');
  await expect(second.getByText('Время вышло', { exact: true })).toHaveCount(1);

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
